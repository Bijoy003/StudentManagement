using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Infrastructure.Repositories;

namespace StudentManagement.Tests;

public class ErrorLogRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

    private static DbContextOptions<ReadOnlyDbContext> CreateReadOnlyOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ReadOnlyDbContext>().UseSqlite(connection).Options;

    [Fact]
    public async Task AddAsync_NewError_ReturnsPersistedRow()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        {
            await context.Database.EnsureCreatedAsync();
        }

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        using (var readOnlyContext = new ReadOnlyDbContext(CreateReadOnlyOptions(connection)))
        {
            var repository = new ErrorLogRepository(context, readOnlyContext);

            await repository.AddAsync(new ErrorLog
            {
                TimestampUtc = new DateTime(2026, 11, 19, 10, 0, 0, DateTimeKind.Utc),
                Source = "/Chat/Send",
                UserId = "user-1",
                ExceptionType = "System.InvalidOperationException",
                Message = "Provider is down",
                StackTrace = "at ChatService.GetReplyAsync()",
                TraceId = "trace-1"
            });

            var rows = await repository.GetRecentReadOnlyAsync(10, null, null);
            var row = Assert.Single(rows);
            Assert.Equal("/Chat/Send", row.Source);
            Assert.Equal("user-1", row.UserId);
            Assert.Equal("Provider is down", row.Message);
            Assert.Equal("trace-1", row.TraceId);
            Assert.True(row.Id > 0);
        }

        await connection.CloseAsync();
    }

    [Fact]
    public async Task GetRecentReadOnlyAsync_MultipleErrors_ReturnsNewestFirst()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        {
            await context.Database.EnsureCreatedAsync();
            context.ErrorLogs.AddRange(
                new ErrorLog { TimestampUtc = new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc), Source = "/a", Message = "older" },
                new ErrorLog { TimestampUtc = new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc), Source = "/b", Message = "newer" },
                new ErrorLog { TimestampUtc = new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc), Source = "/c", Message = "middle" });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        using (var readOnlyContext = new ReadOnlyDbContext(CreateReadOnlyOptions(connection)))
        {
            var repository = new ErrorLogRepository(context, readOnlyContext);

            var rows = await repository.GetRecentReadOnlyAsync(10, null, null);

            Assert.Equal(new[] { "newer", "middle", "older" }, rows.Select(r => r.Message));
        }

        await connection.CloseAsync();
    }

    [Fact]
    public async Task GetRecentReadOnlyAsync_TakeLimit_ReturnsLimitedRows()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        {
            await context.Database.EnsureCreatedAsync();
            for (var i = 0; i < 5; i++)
            {
                context.ErrorLogs.Add(new ErrorLog
                {
                    TimestampUtc = new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                    Source = "/a",
                    Message = $"error-{i}"
                });
            }
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        using (var readOnlyContext = new ReadOnlyDbContext(CreateReadOnlyOptions(connection)))
        {
            var repository = new ErrorLogRepository(context, readOnlyContext);

            var rows = await repository.GetRecentReadOnlyAsync(2, null, null);

            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { "error-4", "error-3" }, rows.Select(r => r.Message));
        }

        await connection.CloseAsync();
    }

    [Fact]
    public async Task GetRecentReadOnlyAsync_SourceFilter_ReturnsMatchingRows()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        {
            await context.Database.EnsureCreatedAsync();
            context.ErrorLogs.AddRange(
                new ErrorLog { TimestampUtc = DateTime.UtcNow, Source = "/Chat/Send", Message = "chat failure" },
                new ErrorLog { TimestampUtc = DateTime.UtcNow, Source = "/Student/Save", Message = "student failure" });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        using (var readOnlyContext = new ReadOnlyDbContext(CreateReadOnlyOptions(connection)))
        {
            var repository = new ErrorLogRepository(context, readOnlyContext);

            var rows = await repository.GetRecentReadOnlyAsync(10, "Chat", null);

            var row = Assert.Single(rows);
            Assert.Equal("chat failure", row.Message);
        }

        await connection.CloseAsync();
    }

    [Fact]
    public async Task GetRecentReadOnlyAsync_SinceFilter_ReturnsRecentRows()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        {
            await context.Database.EnsureCreatedAsync();
            context.ErrorLogs.AddRange(
                new ErrorLog { TimestampUtc = new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc), Source = "/a", Message = "old" },
                new ErrorLog { TimestampUtc = new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc), Source = "/a", Message = "recent" });
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        using (var readOnlyContext = new ReadOnlyDbContext(CreateReadOnlyOptions(connection)))
        {
            var repository = new ErrorLogRepository(context, readOnlyContext);

            var rows = await repository.GetRecentReadOnlyAsync(10, null, new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc));

            var row = Assert.Single(rows);
            Assert.Equal("recent", row.Message);
        }

        await connection.CloseAsync();
    }
}