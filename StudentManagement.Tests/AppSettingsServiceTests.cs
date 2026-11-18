using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StudentManagement.Application;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

public class AppSettingsServiceTests
{
    private static readonly string[] AllKeys =
    [
        "Chat:Enabled", "Chat:IncludeRag", "Chat:IncludeTools", "Chat:IncludeFullDocumentation",
        "Chat:MaxRagChunks", "Chat:McpEnabled", "Chat:DailyTokenLimit"
    ];

    [Fact]
    public async Task GetChatSettingsAsync_NoDbRows_ReturnsConfigDefaults()
    {
        // Arrange
        var repo = new Mock<IAppSettingRepository>();
        repo.Setup(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AppSetting>());
        var service = CreateService(
            repo,
            new ChatFeatureOptions { IncludeRag = true, IncludeTools = true, IncludeFullDocumentation = true, MaxRagChunks = 7 },
            new McpOptions { Enabled = true });

        // Act
        var settings = await service.GetChatSettingsAsync();

        // Assert
        Assert.True(settings.ChatEnabled);
        Assert.True(settings.IncludeRag);
        Assert.True(settings.IncludeTools);
        Assert.True(settings.IncludeFullDocumentation);
        Assert.Equal(7, settings.MaxRagChunks);
        Assert.True(settings.McpEnabled);
        Assert.Equal(20000, settings.DailyTokenLimit);
    }

    [Fact]
    public async Task GetChatSettingsAsync_DbRowsOverrideDefaults_ReturnsDbValues()
    {
        // Arrange
        var repo = new Mock<IAppSettingRepository>();
        repo.Setup(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Row("Chat:Enabled", "false"),
                Row("Chat:IncludeRag", "true"),
                Row("Chat:IncludeTools", "true"),
                Row("Chat:IncludeFullDocumentation", "true"),
                Row("Chat:MaxRagChunks", "9"),
                Row("Chat:McpEnabled", "true"),
                Row("Chat:DailyTokenLimit", "5000")
            });
        var service = CreateService(repo);

        // Act
        var settings = await service.GetChatSettingsAsync();

        // Assert
        Assert.False(settings.ChatEnabled);
        Assert.True(settings.IncludeRag);
        Assert.True(settings.IncludeTools);
        Assert.True(settings.IncludeFullDocumentation);
        Assert.Equal(9, settings.MaxRagChunks);
        Assert.True(settings.McpEnabled);
        Assert.Equal(5000, settings.DailyTokenLimit);
    }

    [Fact]
    public async Task GetChatSettingsAsync_PartialDbRows_ReturnsMergedSettings()
    {
        // Arrange
        var repo = new Mock<IAppSettingRepository>();
        repo.Setup(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Row("Chat:IncludeTools", "true"),
                Row("Chat:DailyTokenLimit", "1234")
            });
        var service = CreateService(repo, new ChatFeatureOptions { IncludeRag = true, MaxRagChunks = 5 });

        // Act
        var settings = await service.GetChatSettingsAsync();

        // Assert — DB rows win where present, config defaults fill the gaps
        Assert.True(settings.ChatEnabled);
        Assert.True(settings.IncludeRag);
        Assert.True(settings.IncludeTools);
        Assert.False(settings.IncludeFullDocumentation);
        Assert.Equal(5, settings.MaxRagChunks);
        Assert.False(settings.McpEnabled);
        Assert.Equal(1234, settings.DailyTokenLimit);
    }

    [Fact]
    public async Task GetChatSettingsAsync_UnparseableDbValue_ReturnsDefaultForThatKey()
    {
        // Arrange
        var repo = new Mock<IAppSettingRepository>();
        repo.Setup(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                Row("Chat:IncludeRag", "not-a-bool"),
                Row("Chat:MaxRagChunks", "abc"),
                Row("Chat:McpEnabled", "maybe"),
                Row("Chat:DailyTokenLimit", "lots")
            });
        var service = CreateService(
            repo,
            new ChatFeatureOptions { IncludeRag = true, MaxRagChunks = 7 },
            new McpOptions { Enabled = true });

        // Act
        var settings = await service.GetChatSettingsAsync();

        // Assert
        Assert.True(settings.IncludeRag);
        Assert.Equal(7, settings.MaxRagChunks);
        Assert.True(settings.McpEnabled);
        Assert.Equal(20000, settings.DailyTokenLimit);
    }

    [Fact]
    public async Task GetChatSettingsAsync_CacheHit_ReturnsWithoutRepositoryCall()
    {
        // Arrange
        var repo = new Mock<IAppSettingRepository>();
        repo.Setup(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Row("Chat:DailyTokenLimit", "4242") });
        var service = CreateService(repo);

        // Act — first read primes the cache, second read must be served from it
        var first = await service.GetChatSettingsAsync();
        var second = await service.GetChatSettingsAsync();

        // Assert
        Assert.Equal(4242, first.DailyTokenLimit);
        Assert.Equal(first.DailyTokenLimit, second.DailyTokenLimit);
        Assert.Equal(first.ChatEnabled, second.ChatEnabled);
        repo.Verify(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveChatSettingsAsync_ValidSettings_ReturnsUpsertedKeysWithUserAndTimestamp()
    {
        // Arrange
        var repo = new Mock<IAppSettingRepository>();
        IReadOnlyList<AppSetting>? upserted = null;
        repo.Setup(r => r.UpsertRangeAsync(It.IsAny<IEnumerable<AppSetting>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AppSetting>, CancellationToken>((settings, _) => upserted = settings.ToList())
            .Returns(Task.CompletedTask);
        var service = CreateService(repo);
        var settings = new ChatSettings
        {
            ChatEnabled = false,
            IncludeRag = true,
            IncludeTools = true,
            IncludeFullDocumentation = true,
            MaxRagChunks = 12,
            McpEnabled = true,
            DailyTokenLimit = 500
        };

        // Act
        var before = DateTime.UtcNow;
        await service.SaveChatSettingsAsync(settings, "admin-1");
        var after = DateTime.UtcNow;

        // Assert — all seven keys upserted atomically with caller id and a fresh timestamp
        repo.Verify(r => r.UpsertRangeAsync(It.IsAny<IEnumerable<AppSetting>>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(upserted);
        Assert.Equal(AllKeys.OrderBy(k => k), upserted!.Select(s => s.Key).OrderBy(k => k));
        Assert.All(upserted, s => Assert.Equal("admin-1", s.UpdatedByUserId));
        Assert.All(upserted, s => Assert.InRange(s.UpdatedAtUtc, before, after));

        var byKey = upserted.ToDictionary(s => s.Key);
        Assert.False(bool.Parse(byKey["Chat:Enabled"].Value));
        Assert.True(bool.Parse(byKey["Chat:IncludeRag"].Value));
        Assert.True(bool.Parse(byKey["Chat:IncludeTools"].Value));
        Assert.True(bool.Parse(byKey["Chat:IncludeFullDocumentation"].Value));
        Assert.Equal(12, int.Parse(byKey["Chat:MaxRagChunks"].Value));
        Assert.True(bool.Parse(byKey["Chat:McpEnabled"].Value));
        Assert.Equal(500, int.Parse(byKey["Chat:DailyTokenLimit"].Value));
    }

    [Fact]
    public async Task SaveChatSettingsAsync_AfterSave_ReturnsNewSettingsOnNextRead()
    {
        // Arrange — the mocked repo behaves like a real store: upserts mutate the rows
        var rows = new List<AppSetting>();
        var repo = new Mock<IAppSettingRepository>();
        repo.Setup(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (IReadOnlyList<AppSetting>)rows.ToList());
        repo.Setup(r => r.UpsertRangeAsync(It.IsAny<IEnumerable<AppSetting>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<AppSetting>, CancellationToken>((settings, _) =>
            {
                foreach (var setting in settings)
                {
                    rows.RemoveAll(r => r.Key == setting.Key);
                    rows.Add(setting);
                }
            })
            .Returns(Task.CompletedTask);
        var service = CreateService(repo);

        // Act — read (primes cache), save, read again
        var initial = await service.GetChatSettingsAsync();
        await service.SaveChatSettingsAsync(
            new ChatSettings { IncludeRag = true, MaxRagChunks = 9, DailyTokenLimit = 777 },
            "admin-1");
        var updated = await service.GetChatSettingsAsync();

        // Assert — the save invalidated the cache, so the second read sees the new values
        Assert.False(initial.IncludeRag);
        Assert.True(updated.IncludeRag);
        Assert.Equal(9, updated.MaxRagChunks);
        Assert.Equal(777, updated.DailyTokenLimit);
        repo.Verify(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    public async Task SaveChatSettingsAsync_MaxRagChunksOutOfRange_ThrowsArgumentOutOfRangeException(int maxRagChunks)
    {
        // Arrange
        var repo = new Mock<IAppSettingRepository>();
        var service = CreateService(repo);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SaveChatSettingsAsync(new ChatSettings { MaxRagChunks = maxRagChunks }, "admin-1"));
        repo.Verify(r => r.UpsertRangeAsync(It.IsAny<IEnumerable<AppSetting>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SaveChatSettingsAsync_NegativeDailyTokenLimit_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var repo = new Mock<IAppSettingRepository>();
        var service = CreateService(repo);

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            service.SaveChatSettingsAsync(new ChatSettings { DailyTokenLimit = -1 }, "admin-1"));
        repo.Verify(r => r.UpsertRangeAsync(It.IsAny<IEnumerable<AppSetting>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetChatSettingsAsync_RepositoryThrows_ReturnsConfigDefaults()
    {
        // Arrange
        var repo = new Mock<IAppSettingRepository>();
        repo.Setup(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database unreachable"));
        var service = CreateService(repo, new ChatFeatureOptions { IncludeRag = true, MaxRagChunks = 6 });

        // Act
        var settings = await service.GetChatSettingsAsync();

        // Assert — configured defaults, and the fallback was NOT cached (next read retries the repo)
        Assert.True(settings.IncludeRag);
        Assert.Equal(6, settings.MaxRagChunks);
        Assert.True(settings.ChatEnabled);
        await service.GetChatSettingsAsync();
        repo.Verify(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetChatSettingsAsync_CorruptCacheEntry_ReturnsDbValues()
    {
        // Arrange
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        await cache.SetStringAsync(CacheKeys.ChatSettings, "this-is-not-json");
        var repo = new Mock<IAppSettingRepository>();
        repo.Setup(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Row("Chat:DailyTokenLimit", "4242") });
        var service = CreateService(repo, cache: cache);

        // Act
        var settings = await service.GetChatSettingsAsync();

        // Assert — corrupt entry ignored, DB values win
        Assert.Equal(4242, settings.DailyTokenLimit);
        repo.Verify(r => r.GetAllReadOnlyAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AppSetting Row(string key, string value) => new()
    {
        Key = key,
        Value = value,
        UpdatedAtUtc = DateTime.UtcNow
    };

    private static AppSettingsService CreateService(
        Mock<IAppSettingRepository> repo,
        ChatFeatureOptions? features = null,
        McpOptions? mcp = null,
        IDistributedCache? cache = null)
    {
        return new AppSettingsService(
            repo.Object,
            Options.Create(features ?? new ChatFeatureOptions()),
            Options.Create(mcp ?? new McpOptions()),
            cache ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            NullLogger<AppSettingsService>.Instance);
    }
}
