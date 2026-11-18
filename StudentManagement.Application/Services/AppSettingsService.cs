using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Application.Services;

public class AppSettingsService : IAppSettingsService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    private readonly IAppSettingRepository _repository;
    private readonly ChatFeatureOptions _featureDefaults;
    private readonly McpOptions _mcpDefaults;
    private readonly IDistributedCache _cache;
    private readonly ILogger<AppSettingsService> _logger;

    public AppSettingsService(
        IAppSettingRepository repository,
        IOptions<ChatFeatureOptions> featureDefaults,
        IOptions<McpOptions> mcpDefaults,
        IDistributedCache cache,
        ILogger<AppSettingsService> logger)
    {
        _repository = repository;
        _featureDefaults = featureDefaults.Value;
        _mcpDefaults = mcpDefaults.Value;
        _cache = cache;
        _logger = logger;
    }

    public async Task<ChatSettings> GetChatSettingsAsync(CancellationToken cancellationToken = default)
    {
        var cached = await _cache.GetStringAsync(CacheKeys.ChatSettings, cancellationToken);
        if (cached is not null)
        {
            try
            {
                var deserialized = JsonSerializer.Deserialize<ChatSettings>(cached);
                if (deserialized is not null)
                {
                    return deserialized;
                }
            }
            catch (JsonException)
            {
                // Corrupt cache entry: fall through and reload from the database.
            }
        }

        var settings = BuildDefaults();
        try
        {
            var rows = await _repository.GetAllReadOnlyAsync(cancellationToken);
            foreach (var row in rows)
            {
                Apply(settings, row);
            }
        }
        catch (Exception ex)
        {
            // Settings are an overlay, not a hard dependency: an unreachable table
            // downgrades to configured defaults instead of failing every chat request.
            _logger.LogWarning(ex, "Loading chat settings from the database failed; using configured defaults.");
            return settings;
        }

        await _cache.SetStringAsync(
            CacheKeys.ChatSettings,
            JsonSerializer.Serialize(settings),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
            cancellationToken);

        return settings;
    }

    public async Task SaveChatSettingsAsync(ChatSettings settings, string? updatedByUserId, CancellationToken cancellationToken = default)
    {
        if (settings.MaxRagChunks is < 1 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.MaxRagChunks, "MaxRagChunks must be between 1 and 20.");
        }

        if (settings.DailyTokenLimit is < 0 or > 10_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.DailyTokenLimit, "DailyTokenLimit must be between 0 and 10,000,000.");
        }

        var updatedAtUtc = DateTime.UtcNow;
        var rows = ToRows(settings)
            .Select(row => new AppSetting
            {
                Key = row.Key,
                Value = row.Value,
                UpdatedAtUtc = updatedAtUtc,
                UpdatedByUserId = updatedByUserId
            })
            .ToList();

        // Double-delete around the atomic write: a reader that loaded the old rows just
        // before the save cannot re-populate the cache with a stale snapshot afterwards.
        await _cache.RemoveAsync(CacheKeys.ChatSettings, cancellationToken);
        try
        {
            await _repository.UpsertRangeAsync(rows, cancellationToken);
        }
        finally
        {
            await _cache.RemoveAsync(CacheKeys.ChatSettings, cancellationToken);
        }
    }

    private ChatSettings BuildDefaults() => new()
    {
        ChatEnabled = true,
        IncludeRag = _featureDefaults.IncludeRag,
        IncludeTools = _featureDefaults.IncludeTools,
        IncludeFullDocumentation = _featureDefaults.IncludeFullDocumentation,
        MaxRagChunks = _featureDefaults.MaxRagChunks,
        McpEnabled = _mcpDefaults.Enabled,
        DailyTokenLimit = 20000
    };

    private static void Apply(ChatSettings settings, AppSetting row)
    {
        switch (row.Key)
        {
            case "Chat:Enabled":
                if (bool.TryParse(row.Value, out var chatEnabled))
                {
                    settings.ChatEnabled = chatEnabled;
                }
                break;
            case "Chat:IncludeRag":
                if (bool.TryParse(row.Value, out var includeRag))
                {
                    settings.IncludeRag = includeRag;
                }
                break;
            case "Chat:IncludeTools":
                if (bool.TryParse(row.Value, out var includeTools))
                {
                    settings.IncludeTools = includeTools;
                }
                break;
            case "Chat:IncludeFullDocumentation":
                if (bool.TryParse(row.Value, out var includeFullDocumentation))
                {
                    settings.IncludeFullDocumentation = includeFullDocumentation;
                }
                break;
            case "Chat:MaxRagChunks":
                if (int.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxRagChunks)
                    && maxRagChunks is >= 1 and <= 20)
                {
                    settings.MaxRagChunks = maxRagChunks;
                }
                break;
            case "Chat:McpEnabled":
                if (bool.TryParse(row.Value, out var mcpEnabled))
                {
                    settings.McpEnabled = mcpEnabled;
                }
                break;
            case "Chat:DailyTokenLimit":
                if (int.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dailyTokenLimit)
                    && dailyTokenLimit is >= 0 and <= 10_000_000)
                {
                    settings.DailyTokenLimit = dailyTokenLimit;
                }
                break;
        }
    }

    private static IEnumerable<(string Key, string Value)> ToRows(ChatSettings settings)
    {
        yield return ("Chat:Enabled", settings.ChatEnabled ? "true" : "false");
        yield return ("Chat:IncludeRag", settings.IncludeRag ? "true" : "false");
        yield return ("Chat:IncludeTools", settings.IncludeTools ? "true" : "false");
        yield return ("Chat:IncludeFullDocumentation", settings.IncludeFullDocumentation ? "true" : "false");
        yield return ("Chat:MaxRagChunks", settings.MaxRagChunks.ToString(CultureInfo.InvariantCulture));
        yield return ("Chat:McpEnabled", settings.McpEnabled ? "true" : "false");
        yield return ("Chat:DailyTokenLimit", settings.DailyTokenLimit.ToString(CultureInfo.InvariantCulture));
    }
}
