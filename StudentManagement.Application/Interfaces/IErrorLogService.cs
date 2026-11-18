using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Interfaces;

public interface IErrorLogService
{
    // Best-effort: implementations must never throw — a logging failure cannot mask
    // the original exception that is being reported.
    Task LogAsync(string source, Exception exception, string? userId, string? traceId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ErrorLog>> GetRecentAsync(int take = 200, string? source = null, DateTime? sinceUtc = null, CancellationToken cancellationToken = default);
}
