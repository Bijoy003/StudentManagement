using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Infrastructure.Repositories;

namespace StudentManagement.Tests;

public class ChatUsageRepositoryTests
{
    private static DbContextOptions<ApplicationDbContext> CreateOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options;

    private static DbContextOptions<ReadOnlyDbContext> CreateReadOnlyOptions(SqliteConnection connection) =>
        new DbContextOptionsBuilder<ReadOnlyDbContext>().UseSqlite(connection).Options;

    [Fact]
    public async Task AddAsync_NewUsage_ReturnsPersistedRow()
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
            var repository = new ChatUsageRepository(context, readOnlyContext);

            await repository.AddAsync(new ChatUsageLog
            {
                UserId = "user-1",
                UsageDateUtc = DateTime.UtcNow.Date,
                InputTokens = 100,
                OutputTokens = 50,
                TotalTokens = 150,
                Model = "qwen2.5:3b-instruct",
                ElapsedMs = 3200,
                CreatedAtUtc = DateTime.UtcNow
            });

            var total = await repository.GetTotalTokensForUserOnDateReadOnlyAsync("user-1", DateTime.UtcNow.Date);
            Assert.Equal(150, total);
        }

        await connection.CloseAsync();
    }

    [Fact]
    public async Task GetTotalTokensForUserOnDateReadOnlyAsync_MatchingUserAndDate_ReturnsSum()
    {
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();
        var today = DateTime.UtcNow.Date;

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        {
            await context.Database.EnsureCreatedAsync();
            context.ChatUsageLogs.AddRange(
                Row("user-1", today, 100),
                Row("user-1", today, 250),
                Row("user-2", today, 999),
                Row("user-1", today.AddDays(-1), 500));
            await context.SaveChangesAsync();
        }

        using (var context = new ApplicationDbContext(CreateOptions(connection)))
        using (var readOnlyContext = new ReadOnlyDbContext(CreateReadOnlyOptions(connection)))
        {
            var repository = new ChatUsageRepository(context, readOnlyContext);

            var total = await repository.GetTotalTokensForUserOnDateReadOnlyAsync("user-1", today);

            Assert.Equal(350, total);
        }

        await connection.CloseAsync();
    }

    [Fact]
    public async Task GetTotalTokensForUserOnDateReadOnlyAsync_NoMatchingRows_ReturnsZero()
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
            var repository = new ChatUsageRepository(context, readOnlyContext);

            var total = await repository.GetTotalTokensForUserOnDateReadOnlyAsync("user-1", DateTime.UtcNow.Date);

            Assert.Equal(0, total);
        }

        await connection.CloseAsync();
    }

    private static ChatUsageLog Row(string userId, DateTime date, int totalTokens) => new()
    {
        UserId = userId,
        UsageDateUtc = date,
        InputTokens = totalTokens / 2,
        OutputTokens = totalTokens - totalTokens / 2,
        TotalTokens = totalTokens,
        Model = "test-model",
        ElapsedMs = 10,
        CreatedAtUtc = DateTime.UtcNow
    };
}