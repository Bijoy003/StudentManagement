using Microsoft.Extensions.Logging;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Application.Services;

public class ErrorLogService : IErrorLogService
{
    private const int MaxMessageLength = 4000;
    private const int MaxSourceLength = 512;
    private const int MaxExceptionTypeLength = 512;
    private const int MaxUserIdLength = 450;
    private const int MaxTraceIdLength = 128;

    private readonly IErrorLogRepository _repository;
    private readonly ILogger<ErrorLogService> _logger;

    public ErrorLogService(IErrorLogRepository repository, ILogger<ErrorLogService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task LogAsync(string source, Exception exception, string? userId, string? traceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var message = Truncate(exception.Message, MaxMessageLength);
            var exceptionType = Truncate(exception.GetType().FullName, MaxExceptionTypeLength);
            var userIdClamped = Truncate(userId, MaxUserIdLength);
            var traceIdClamped = Truncate(traceId, MaxTraceIdLength);

            await _repository.AddAsync(new ErrorLog
            {
                TimestampUtc = DateTime.UtcNow,
                Source = Truncate(source ?? "/", MaxSourceLength),
                UserId = userIdClamped,
                ExceptionType = exceptionType,
                Message = message,
                StackTrace = exception.StackTrace,
                TraceId = traceIdClamped
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Writing to the error log table failed; the original exception is unaffected.");
        }
    }

    public Task<IReadOnlyList<ErrorLog>> GetRecentAsync(int take = 200, string? source = null, DateTime? sinceUtc = null, CancellationToken cancellationToken = default) =>
        _repository.GetRecentReadOnlyAsync(take, source, sinceUtc, cancellationToken);

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
