using Microsoft.EntityFrameworkCore;
using StudentManagement.Application.Validation;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Infrastructure.Repositories;
using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class StudentServiceIntegrationTests : SqlServerIntegrationTest
{
    public StudentServiceIntegrationTests(SqlServerFixture fixture) : base(fixture)
    {
    }

    [SqlServerFact]
    public async Task SaveStudentAsync_NewStudent_ReturnsPersistedRowWithGeneratedId()
    {
        // Arrange
        var sut = CreateStudentService();
        var student = TestData.Student(name: "Ada Lovelace", email: "ada@example.com");

        // Act
        await sut.SaveStudentAsync(student);

        // Assert — SQL Server assigned the identity, and the row survived the commit
        Assert.True(student.Id > 0, "Expected the database to generate an Id for the new student.");
        Assert.Equal(1, await WriteContext.Students.CountAsync());

        await using var verification = Database.CreateWriteContext();
        var persisted = await verification.Students.SingleAsync(s => s.Id == student.Id);
        Assert.Equal("Ada Lovelace", persisted.Name);
        Assert.Equal("ada@example.com", persisted.Email);
    }

    [SqlServerFact]
    public async Task DeleteStudentAsync_StudentWithEnrollments_ReturnsEnrollmentsCascaded()
    {
        // Arrange — student enrolled in two courses
        var student = TestData.Student(name: "Grace Hopper", email: "grace@example.com");
        var algebra = TestData.Course(name: "Algebra");
        var physics = TestData.Course(name: "Physics");

        WriteContext.Students.Add(student);
        WriteContext.Courses.AddRange(algebra, physics);
        await WriteContext.SaveChangesAsync();

        WriteContext.Enrollments.AddRange(
            TestData.Enrollment(student.Id, algebra.Id, grade: 3.5m),
            TestData.Enrollment(student.Id, physics.Id, grade: 4.0m));
        await WriteContext.SaveChangesAsync();

        var sut = CreateStudentService();

        // Act
        await sut.DeleteStudentAsync(student.Id);

        // Assert — the FK cascade removed the enrollments but kept the courses
        await using var verification = Database.CreateWriteContext();
        Assert.False(await verification.Students.AnyAsync(s => s.Id == student.Id));
        Assert.Equal(0, await verification.Enrollments.CountAsync());
        Assert.Equal(2, await verification.Courses.CountAsync());
    }

    [SqlServerFact]
    public async Task MigrateAsync_NewDatabase_ReturnsNoPendingMigrationsAndQueryableTables()
    {
        // Arrange / Act — the fixture already ran MigrateAsync on a brand-new database
        await using var context = Database.CreateWriteContext();

        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();

        // Assert — every migration applied, and the core tables match the model
        Assert.NotEmpty(applied);
        Assert.Empty(pending);

        Assert.Equal(0, await context.Students.CountAsync());
        Assert.Equal(0, await context.Courses.CountAsync());
        Assert.Equal(0, await context.Enrollments.CountAsync());
        Assert.Equal(0, await context.Set<ApplicationUser>().CountAsync());
    }

    [SqlServerFact]
    public async Task SaveStudentAsync_DuplicateEmail_ReturnsValidationExceptionWithoutInserting()
    {
        // Arrange — the real chain (required fields -> format -> duplicate email query)
        var existing = TestData.Student(email: "taken@example.com");
        await SeedAsync(db =>
        {
            db.Students.Add(existing);
            return Task.CompletedTask;
        });

        var requiredFields = new RequiredFieldsRule();
        requiredFields
            .SetNext(new EmailFormatRule())
            .SetNext(new DuplicateEmailRule(new StudentRepository(WriteContext, ReadOnlyContext)));

        var sut = CreateStudentService(requiredFields);

        // Act — different casing must still collide (rule compares OrdinalIgnoreCase)
        var exception = await Assert.ThrowsAsync<ValidationException>(
            () => sut.SaveStudentAsync(TestData.Student(email: "TAKEN@example.com")));

        // Assert — rejected before any write
        Assert.Contains("already registered", exception.Message);

        await using var verification = Database.CreateWriteContext();
        Assert.Equal(1, await verification.Students.CountAsync());
    }

    [SqlServerFact]
    public async Task SaveStudentAsync_AfterCachedRead_ReturnsFreshRowFromDatabase()
    {
        // Arrange — prime the cache, exactly what a prior page load would do
        var student = TestData.Student(name: "Ada Lovelace", email: "ada@example.com");
        await SeedAsync(db =>
        {
            db.Students.Add(student);
            return Task.CompletedTask;
        });

        var sut = CreateStudentService();
        var cached = await sut.GetStudentByIdAsync(student.Id);
        Assert.Equal("Ada Lovelace", cached!.Name);

        // Act — write must evict the entry the read just cached
        student.Name = "Ada King";
        await sut.SaveStudentAsync(student);
        var refreshed = await sut.GetStudentByIdAsync(student.Id);

        // Assert — a stale cache would still say "Ada Lovelace"
        Assert.Equal("Ada King", refreshed!.Name);
    }

    [SqlServerFact]
    public async Task GetStudentsEnrolledInMoreThan_SeededEnrollments_ReturnsStudentsAboveThreshold()
    {
        // Arrange — Ada in 3 courses, Grace in 1
        var ada = TestData.Student(name: "Ada Lovelace", email: "ada@example.com");
        var grace = TestData.Student(name: "Grace Hopper", email: "grace@example.com");
        var algebra = TestData.Course(name: "Algebra");
        var geometry = TestData.Course(name: "Geometry");
        var physics = TestData.Course(name: "Physics");

        await SeedAsync(db =>
        {
            db.Students.AddRange(ada, grace);
            db.Courses.AddRange(algebra, geometry, physics);
            return Task.CompletedTask;
        });

        await SeedAsync(db =>
        {
            db.Enrollments.AddRange(
                TestData.Enrollment(ada.Id, algebra.Id),
                TestData.Enrollment(ada.Id, geometry.Id),
                TestData.Enrollment(ada.Id, physics.Id),
                TestData.Enrollment(grace.Id, algebra.Id));
            return Task.CompletedTask;
        });

        var sut = CreateStudentService();

        // Act
        var result = await sut.GetStudentsEnrolledInMoreThan(1);

        // Assert — the count runs server-side against the enrollment table
        var only = Assert.Single(result);
        Assert.Equal(ada.Id, only.Id);
        Assert.Equal(3, only.Enrollments.Count);
    }
}
