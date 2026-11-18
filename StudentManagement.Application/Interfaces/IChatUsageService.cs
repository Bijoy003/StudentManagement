using StudentManagement.Application.Configuration;

namespace StudentManagement.Application.Interfaces;

public interface IChatUsageService
{
    Task<int> GetUsedTodayAsync(string userId, CancellationToken cancellationToken = default);
    Task RecordAsync(string userId, int inputTokens, int outputTokens, string model, long elapsedMs, CancellationToken cancellationToken = default);
    Task<ChatQuotaStatus> GetQuotaStatusAsync(string userId, bool isAdmin, int dailyLimit, CancellationToken cancellationToken = default);
}
