using Microsoft.Extensions.Logging;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Application.Services;

public class ChatUsageService : IChatUsageService
{
    private readonly IChatUsageRepository _repository;
    private readonly ILogger<ChatUsageService> _logger;

    public ChatUsageService(IChatUsageRepository repository, ILogger<ChatUsageService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public Task<int> GetUsedTodayAsync(string userId, CancellationToken cancellationToken = default) =>
        _repository.GetTotalTokensForUserOnDateReadOnlyAsync(userId, DateTime.UtcNow.Date, cancellationToken);

    public async Task RecordAsync(string userId, int inputTokens, int outputTokens, string model, long elapsedMs, CancellationToken cancellationToken = default)
    {
        await _repository.AddAsync(new ChatUsageLog
        {
            UserId = userId,
            UsageDateUtc = DateTime.UtcNow.Date,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            TotalTokens = inputTokens + outputTokens,
            Model = model,
            ElapsedMs = elapsedMs,
            CreatedAtUtc = DateTime.UtcNow
        }, cancellationToken);

        _logger.LogDebug(
            "Recorded chat usage for {UserId}: {InputTokens} in / {OutputTokens} out ({ElapsedMs}ms).",
            userId, inputTokens, outputTokens, elapsedMs);
    }

    public async Task<ChatQuotaStatus> GetQuotaStatusAsync(string userId, bool isAdmin, int dailyLimit, CancellationToken cancellationToken = default)
    {
        var resetsAtUtc = DateTime.UtcNow.Date.AddDays(1);
        if (isAdmin)
        {
            return new ChatQuotaStatus(0, dailyLimit, true, resetsAtUtc);
        }

        var used = await GetUsedTodayAsync(userId, cancellationToken);
        return new ChatQuotaStatus(used, dailyLimit, false, resetsAtUtc);
    }
}
