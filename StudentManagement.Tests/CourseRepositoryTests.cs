using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Infrastructure.Repositories;
using System.Collections.Concurrent;
using Xunit.Abstractions;

namespace StudentManagement.Tests
{
    public class CourseRepositoryTests
    {
        private readonly ITestOutputHelper _output;

        public CourseRepositoryTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private DbContextOptions<ApplicationDbContext> CreateOptions(SqliteConnection connection)
        {
            return new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
        }

        [Fact]
        public async Task GetByIdAsync_WhenCourseExists_ReturnsCourse()
        {
            // Arrange
            var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();

            var options = CreateOptions(connection);

            using (var context = new ApplicationDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();

                context.Courses.Add(new Course { Id = 1, Name = "Math" });
                await context.SaveChangesAsync();
            }

            using (var context = new ApplicationDbContext(options))
            {
                var repository = new CourseRepository(context);

                // Act
                var result = await repository.GetByIdAsync(1);

                // Assert
                Assert.NotNull(result);
                Assert.Equal("Math", result.Name);
            }

            await connection.CloseAsync();
        }

        [Fact]
        public async Task GetByIdAsync_WhenCourseDoesNotExist_ReturnsNull()
        {
            var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();

            var options = CreateOptions(connection);

            using (var context = new ApplicationDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
            }

            using (var context = new ApplicationDbContext(options))
            {
                var repository = new CourseRepository(context);

                var result = await repository.GetByIdAsync(99);

                Assert.Null(result);
            }

            await connection.CloseAsync();
        }

        [Fact]
        public async Task UpdateCourse_ConcurrentLoop_ShouldExposeRaceCondition()
        {
            // Arrange
            var connection = new SqliteConnection("Filename=:memory:");
            await connection.OpenAsync();

            var options = CreateOptions(connection);

            // Seed data
            using (var context = new ApplicationDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();

                context.Courses.Add(new Course
                {
                    Id = 1,
                    Name = "0"
                });

                await context.SaveChangesAsync();
            }

            var startSignal = new TaskCompletionSource<bool>();

            // Act: prepare concurrent tasks
            var tasks = Enumerable.Range(0, 20).Select(async i =>
            {
                await startSignal.Task;

                try
                {
                    using var context = new ApplicationDbContext(options);
                    var course = await context.Courses.FindAsync(1);

                    var currentValue = int.Parse(course!.Name);
                    course.Name = (currentValue + 1).ToString();

                    await context.SaveChangesAsync();
                }
                catch (Microsoft.Data.Sqlite.SqliteException) { /* db locked — expected */ }
                catch (DbUpdateException) { /* concurrency conflict — expected */ }

            }).ToList();

            startSignal.SetResult(true);

            await Task.WhenAll(tasks);

            // Assert
            using (var context = new ApplicationDbContext(options))
            {
                var course = await context.Courses.FindAsync(1);
                var finalValue = int.Parse(course!.Name);

                // At least 1 write succeeded, but fewer than 20 proves contention occurred
                Assert.InRange(finalValue, 1, 19);
                _output.WriteLine($"Writes that succeeded: {finalValue}/20");
            }

            await connection.CloseAsync();
        }
    }
}
