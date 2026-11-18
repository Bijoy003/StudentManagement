using StudentManagement.Domain.Entities;

namespace StudentManagement.Domain.Interfaces;

public interface IChatUsageRepository
{
    Task AddAsync(ChatUsageLog log, CancellationToken cancellationToken = default);
    Task<int> GetTotalTokensForUserOnDateReadOnlyAsync(string userId, DateTime usageDateUtc, CancellationToken cancellationToken = default);
}
