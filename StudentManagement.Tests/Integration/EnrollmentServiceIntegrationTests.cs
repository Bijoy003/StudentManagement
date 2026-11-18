using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class EnrollmentServiceIntegrationTests : SqlServerIntegrationTest
{
    public EnrollmentServiceIntegrationTests(SqlServerFixture fixture) : base(fixture)
    {
    }

    [SqlServerFact]
    public async Task SaveEnrollment_NewEnrollment_ReturnsPersistedRowWithNavigations()
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

        var sut = CreateEnrollmentService();
        var enrollment = TestData.Enrollment(student.Id, course.Id, grade: 3.5m);

        // Act
        await sut.SaveEnrollment(enrollment);

        // Assert — identity assigned, and the read path loads Student/Course via Include
        Assert.True(enrollment.Id > 0, "Expected the database to generate an Id for the new enrollment.");

        var only = Assert.Single(await sut.GetEnrollments());
        Assert.Equal(3.5m, only.Grade);
        Assert.Equal("Ada Lovelace", only.Student.Name);
        Assert.Equal("Algebra", only.Course.Name);
    }

    [SqlServerFact]
    public async Task SaveEnrollment_AfterCachedList_ReturnsFreshListFromDatabase()
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

        var sut = CreateEnrollmentService();
        Assert.Empty(await sut.GetEnrollments()); // primes the in-process cache with []

        // Act
        await sut.SaveEnrollment(TestData.Enrollment(student.Id, course.Id, grade: 4.0m));
        var refreshed = (await sut.GetEnrollments()).ToList();

        // Assert — a cache that was not invalidated would still return the empty list
        Assert.Single(refreshed);
        Assert.Equal(4.0m, refreshed[0].Grade);
    }

    [SqlServerFact]
    public async Task GetStudentsInCourse_SeededEnrollments_ReturnsStudentsFromDatabase()
    {
        // Arrange
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
                TestData.Enrollment(grace.Id, physics.Id));
            return Task.CompletedTask;
        });

        var sut = CreateEnrollmentService();

        // Act
        var students = (await sut.GetStudentsInCourse(algebra.Id))
            .OrderBy(s => s.Name)
            .Select(s => s.Name)
            .ToList();

        // Assert — projected through the enrollment table, not the student table
        Assert.Equal(new[] { "Ada Lovelace", "Grace Hopper" }, students);
    }

    [SqlServerFact]
    public async Task GetCoursesForStudent_SeededEnrollments_ReturnsCoursesFromDatabase()
    {
        // Arrange
        var ada = TestData.Student(name: "Ada Lovelace", email: "ada@example.com");
        var algebra = TestData.Course(name: "Algebra");
        var physics = TestData.Course(name: "Physics");
        var geometry = TestData.Course(name: "Geometry");

        await SeedAsync(db =>
        {
            db.Students.Add(ada);
            db.Courses.AddRange(algebra, physics, geometry);
            return Task.CompletedTask;
        });

        await SeedAsync(db =>
        {
            db.Enrollments.AddRange(
                TestData.Enrollment(ada.Id, algebra.Id),
                TestData.Enrollment(ada.Id, physics.Id));
            return Task.CompletedTask;
        });

        var sut = CreateEnrollmentService();

        // Act
        var courses = (await sut.GetCoursesForStudent(ada.Id))
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToList();

        // Assert
        Assert.Equal(new[] { "Algebra", "Physics" }, courses);
    }
}
