using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Infrastructure.Repositories;

namespace StudentManagement.Tests;

public class AppSettingRepositoryTests
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
    public async Task UpsertAsync_NewKey_ReturnsInsertedRow()
    {
        // Arrange
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var readOnlyOptions = CreateReadOnlyOptions(connection);

        using (var context = new ApplicationDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
        }

        using (var context = new ApplicationDbContext(options))
        using (var readOnlyContext = new ReadOnlyDbContext(readOnlyOptions))
        {
            var repository = new AppSettingRepository(context, readOnlyContext);
            var timestamp = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

            // Act
            await repository.UpsertAsync(new AppSetting
            {
                Key = "Chat:Enabled",
                Value = "false",
                UpdatedAtUtc = timestamp,
                UpdatedByUserId = "admin-1"
            });

            // Assert
            var rows = await repository.GetAllReadOnlyAsync();
            var row = Assert.Single(rows);
            Assert.Equal("Chat:Enabled", row.Key);
            Assert.Equal("false", row.Value);
            Assert.Equal("admin-1", row.UpdatedByUserId);
            Assert.Equal(timestamp, row.UpdatedAtUtc);
        }

        await connection.CloseAsync();
    }

    [Fact]
    public async Task UpsertAsync_ExistingKey_ReturnsUpdatedValueAndTimestamp()
    {
        // Arrange
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var readOnlyOptions = CreateReadOnlyOptions(connection);

        using (var context = new ApplicationDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();

            using var seedReadOnlyContext = new ReadOnlyDbContext(readOnlyOptions);
            var seedRepository = new AppSettingRepository(context, seedReadOnlyContext);
            await seedRepository.UpsertAsync(new AppSetting
            {
                Key = "Chat:MaxRagChunks",
                Value = "3",
                UpdatedAtUtc = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedByUserId = "seed-user"
            });
        }

        using (var context = new ApplicationDbContext(options))
        using (var readOnlyContext = new ReadOnlyDbContext(readOnlyOptions))
        {
            var repository = new AppSettingRepository(context, readOnlyContext);
            var newTimestamp = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc);

            // Act
            await repository.UpsertAsync(new AppSetting
            {
                Key = "Chat:MaxRagChunks",
                Value = "9",
                UpdatedAtUtc = newTimestamp,
                UpdatedByUserId = "admin-1"
            });

            // Assert — one row, updated in place (no duplicate key)
            var rows = await repository.GetAllReadOnlyAsync();
            var row = Assert.Single(rows);
            Assert.Equal("Chat:MaxRagChunks", row.Key);
            Assert.Equal("9", row.Value);
            Assert.Equal("admin-1", row.UpdatedByUserId);
            Assert.Equal(newTimestamp, row.UpdatedAtUtc);
        }

        await connection.CloseAsync();
    }

    [Fact]
    public async Task GetAllReadOnlyAsync_MultipleRows_ReturnsAll()
    {
        // Arrange
        var connection = new SqliteConnection("Filename=:memory:");
        await connection.OpenAsync();

        var options = CreateOptions(connection);
        var readOnlyOptions = CreateReadOnlyOptions(connection);

        using (var context = new ApplicationDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();

            using var seedReadOnlyContext = new ReadOnlyDbContext(readOnlyOptions);
            var seedRepository = new AppSettingRepository(context, seedReadOnlyContext);
            await seedRepository.UpsertAsync(new AppSetting { Key = "Chat:Enabled", Value = "true", UpdatedAtUtc = DateTime.UtcNow });
            await seedRepository.UpsertAsync(new AppSetting { Key = "Chat:IncludeRag", Value = "false", UpdatedAtUtc = DateTime.UtcNow });
            await seedRepository.UpsertAsync(new AppSetting { Key = "Chat:DailyTokenLimit", Value = "20000", UpdatedAtUtc = DateTime.UtcNow });
        }

        using (var context = new ApplicationDbContext(options))
        using (var readOnlyContext = new ReadOnlyDbContext(readOnlyOptions))
        {
            var repository = new AppSettingRepository(context, readOnlyContext);

            // Act
            var rows = await repository.GetAllReadOnlyAsync();

            // Assert
            Assert.Equal(3, rows.Count);
            Assert.Equal(
                new[] { "Chat:DailyTokenLimit", "Chat:Enabled", "Chat:IncludeRag" },
                rows.Select(r => r.Key).OrderBy(k => k));

            // Assert — AsNoTracking: nothing entered the read-only context's change tracker
            Assert.All(rows, r => Assert.Equal(EntityState.Detached, readOnlyContext.Entry(r).State));
        }

        await connection.CloseAsync();
    }
}
