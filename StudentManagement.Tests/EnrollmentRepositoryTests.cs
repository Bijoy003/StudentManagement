using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Infrastructure.Repositories;

namespace StudentManagement.Tests;

public class EnrollmentRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> CreateOptions(SqliteConnection connection)
    {
        return new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
    }

    private static DbContextOptions<ReadOnlyDbContext> CreateReadOnlyOptions(SqliteConnection connection)
    {
        return new DbContextOptionsBuilder<ReadOnlyDbContext>()
            .UseSqlite(connection)
            .Options;
    }

    [Fact]
    public async Task GetByCourseIdWithStudentAsyncReadOnly_CourseWithEnrollments_ReturnsEnrollmentsWithStudentPopulated()
    {
        // Arrange
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var readOnlyOptions = CreateReadOnlyOptions(connection);

        using (var context = new ApplicationDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();

            context.Students.AddRange(
                new Student { Id = 1, Name = "Ada Lovelace", Email = "ada@example.com" },
                new Student { Id = 2, Name = "Grace Hopper", Email = "grace@example.com" },
                new Student { Id = 3, Name = "Alan Turing", Email = "alan@example.com" });
            context.Courses.AddRange(
                new Course { Id = 1, Name = "Algebra", Credits = 3, TeacherId = "teacher-a" },
                new Course { Id = 2, Name = "Physics", Credits = 4, TeacherId = "teacher-b" });
            await context.SaveChangesAsync();

            context.Enrollments.AddRange(
                new Enrollment { Id = 1, StudentId = 1, CourseId = 1, Grade = 3.5m },
                new Enrollment { Id = 2, StudentId = 2, CourseId = 1 },
                new Enrollment { Id = 3, StudentId = 3, CourseId = 2, Grade = 4.0m });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(options))
        using (var readOnlyContext = new ReadOnlyDbContext(readOnlyOptions))
        {
            var repository = new EnrollmentRepository(context, readOnlyContext);

            // Act
            var result = await repository.GetByCourseIdWithStudentAsyncReadOnly(1);

            // Assert - filtered to the requested course only
            Assert.Equal(2, result.Count);
            Assert.All(result, e => Assert.Equal(1, e.CourseId));

            // Assert - Include(e => e.Student) populated the navigation on the read-only path
            Assert.All(result, e => Assert.NotNull(e.Student));
            var names = result.OrderBy(e => e.Id).Select(e => e.Student!.Name).ToList();
            Assert.Equal("Ada Lovelace", names[0]);
            Assert.Equal("Grace Hopper", names[1]);

            // Assert - AsNoTracking: nothing entered the read-only context's change tracker
            Assert.All(result, e => Assert.Equal(EntityState.Detached, readOnlyContext.Entry(e).State));
        }

        await connection.CloseAsync();
    }
}
