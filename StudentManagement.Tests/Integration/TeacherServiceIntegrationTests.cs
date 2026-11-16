using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Tests.Integration;

[Collection(SqlServerCollection.Name)]
public class TeacherServiceIntegrationTests : SqlServerIntegrationTest
{
    public TeacherServiceIntegrationTests(SqlServerFixture fixture) : base(fixture)
    {
    }

    [SqlServerFact]
    public async Task UpdateGradeAsync_OwnedCourseEnrollment_ReturnsGradePersisted()
    {
        // Arrange
        var (course, enrollment) = await SeedOwnedCourseWithEnrollment("teacher-a");

        var sut = CreateTeacherService();

        // Act
        var updated = await sut.UpdateGradeAsync(enrollment.Id, 88.5m, "teacher-a");

        // Assert
        Assert.True(updated);

        await using var verification = Database.CreateWriteContext();
        var persisted = await verification.Enrollments.SingleAsync(e => e.Id == enrollment.Id);
        Assert.Equal(88.5m, persisted.Grade);
    }

    [SqlServerFact]
    public async Task UpdateGradeAsync_OtherTeachersCourse_ReturnsFalseAndGradeUnchanged()
    {
        // Arrange
        var (_, enrollment) = await SeedOwnedCourseWithEnrollment("teacher-a");

        var sut = CreateTeacherService();

        // Act
        var updated = await sut.UpdateGradeAsync(enrollment.Id, 88.5m, "teacher-b");

        // Assert — ownership is enforced against the database, not the caller's claim
        Assert.False(updated);

        await using var verification = Database.CreateWriteContext();
        var persisted = await verification.Enrollments.SingleAsync(e => e.Id == enrollment.Id);
        Assert.Null(persisted.Grade);
    }

    [SqlServerFact]
    public async Task DeleteCourseAsync_OwnedCourse_ReturnsCourseAndEnrollmentsRemoved()
    {
        // Arrange
        var (course, _) = await SeedOwnedCourseWithEnrollment("teacher-a");

        var sut = CreateTeacherService();

        // Act
        var deleted = await sut.DeleteCourseAsync(course.Id, "teacher-a");

        // Assert
        Assert.True(deleted);

        await using var verification = Database.CreateWriteContext();
        Assert.False(await verification.Courses.AnyAsync(c => c.Id == course.Id));
        Assert.Equal(0, await verification.Enrollments.CountAsync());
        Assert.Equal(1, await verification.Students.CountAsync());
    }

    private async Task<(Course Course, Enrollment Enrollment)> SeedOwnedCourseWithEnrollment(string teacherId)
    {
        var student = TestData.Student(name: "Ada Lovelace", email: "ada@example.com");
        var course = TestData.Course(name: "Algebra", teacherId: teacherId);

        await SeedAsync(db =>
        {
            db.Students.Add(student);
            db.Courses.Add(course);
            return Task.CompletedTask;
        });

        var enrollment = TestData.Enrollment(student.Id, course.Id);
        await SeedAsync(db =>
        {
            db.Enrollments.Add(enrollment);
            return Task.CompletedTask;
        });

        return (course, enrollment);
    }
}
