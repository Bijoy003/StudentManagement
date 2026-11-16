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
    public async Task SaveCourse_NewCourse_ReturnsPersistedRowWithIgnoredTeacherId()
    {
        // Arrange — the legacy course UI may bind a TeacherId, which the service drops
        var sut = CreateCourseService();
        var course = TestData.Course(name: "Algebra", credits: 4, teacherId: "teacher-a");

        // Act
        await sut.SaveCourse(course);

        // Assert
        Assert.True(course.Id > 0, "Expected the database to generate an Id for the new course.");

        await using var verification = Database.CreateWriteContext();
        var persisted = await verification.Courses.SingleAsync(c => c.Id == course.Id);
        Assert.Equal("Algebra", persisted.Name);
        Assert.Equal(4, persisted.Credits);
        Assert.Null(persisted.TeacherId);
    }

    [SqlServerFact]
    public async Task SaveCourse_ExistingCourseWithTeacher_ReturnsPersistedTeacherIdPreserved()
    {
        // Arrange — seed in its own context so the update path really loads from the database
        int courseId;
        await using (var seed = Database.CreateWriteContext())
        {
            var seeded = TestData.Course(name: "Algebra", teacherId: "teacher-a");
            seed.Courses.Add(seeded);
            await seed.SaveChangesAsync();
            courseId = seeded.Id;
        }

        var sut = CreateCourseService();
        var update = TestData.Course(name: "Algebra II", credits: 5, teacherId: "teacher-b");
        update.Id = courseId;

        // Act
        await sut.SaveCourse(update);

        // Assert — the crafted TeacherId is restamped from the persisted row
        await using var verification = Database.CreateWriteContext();
        var persisted = await verification.Courses.SingleAsync(c => c.Id == courseId);
        Assert.Equal("Algebra II", persisted.Name);
        Assert.Equal(5, persisted.Credits);
        Assert.Equal("teacher-a", persisted.TeacherId);
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
