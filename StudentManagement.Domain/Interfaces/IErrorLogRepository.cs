using StudentManagement.Domain.Entities;

namespace StudentManagement.Domain.Interfaces;

public interface IErrorLogRepository
{
    Task AddAsync(ErrorLog errorLog, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ErrorLog>> GetRecentReadOnlyAsync(int take, string? source, DateTime? sinceUtc, CancellationToken cancellationToken = default);
}
