using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class CourseServiceIntegrationTests : SqlServerIntegrationTest
{
    public CourseServiceIntegrationTests(SqlServerFixture fixture) : base(fixture)
    {
    }

    [SqlServerFact]
    public async Task GetStudentCountPerCourse_SeededEnrollments_ReturnsCountsFromDatabase()
    {
        // Arrange — Algebra has 2 enrollments, Physics has 1
        var ada = TestData.Student(name: "Ada Lovelace", email: "ada@example.com");
        var grace = TestData.Student(name: "Grace Hopper", email: "grace@example.com");
        var algebra = TestData.Course(name: "Algebra");
        var physics = TestData.Course(name: "Physics");

        await SeedAsync(db =>
        {
            db.Students.AddRange(ada, grace);
            db.Courses.AddRange(algebra, physics);
            return Task.CompletedTask;
        });

        await SeedAsync(db =>
        {
            db.Enrollments.AddRange(
                TestData.Enrollment(ada.Id, algebra.Id),
                TestData.Enrollment(grace.Id, algebra.Id),
                TestData.Enrollment(ada.Id, physics.Id));
            return Task.CompletedTask;
        });

        var sut = CreateCourseService();

        // Act
        var counts = (await sut.GetStudentCountPerCourse()).ToList();

        // Assert — the group-by/count runs against the enrollment table
        Assert.Equal(2, counts.Count);
        Assert.Equal(2, counts.Single(c => c.CourseName == "Algebra").StudentCount);
        Assert.Equal(1, counts.Single(c => c.CourseName == "Physics").StudentCount);
    }

    [SqlServerFact]
    public async Task DeleteCourse_CourseWithEnrollments_ReturnsEnrollmentsCascaded()
    {
        // Arrange
        var student = TestData.Student(name: "Ada Lovelace", email: "ada@example.com");
        var course = TestData.Course(name: "Algebra");
        await SeedAsync(db =>
        {
            db.Students.Add(student);
            db.Courses.Add(course);
            return Task.CompletedTask;
        });

        await SeedAsync(db =>
        {
            db.Enrollments.Add(TestData.Enrollment(student.Id, course.Id, grade: 3.5m));
            return Task.CompletedTask;
        });

        var sut = CreateCourseService();

        // Act
        await sut.DeleteCourse(course.Id);

        // Assert — enrollment rows follow the course, the student survives
        await using var verification = Database.CreateWriteContext();
        Assert.False(await verification.Courses.AnyAsync(c => c.Id == course.Id));
        Assert.Equal(0, await verification.Enrollments.CountAsync());
        Assert.Equal(1, await verification.Students.CountAsync());
    }
}
