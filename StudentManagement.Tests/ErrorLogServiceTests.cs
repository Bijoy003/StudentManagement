using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

public class ErrorLogServiceTests
{
    [Fact]
    public async Task LogAsync_Exception_ReturnsPersistedRowWithContext()
    {
        var repo = new Mock<IErrorLogRepository>();
        ErrorLog? captured = null;
        repo.Setup(r => r.AddAsync(It.IsAny<ErrorLog>(), It.IsAny<CancellationToken>()))
            .Callback<ErrorLog, CancellationToken>((log, _) => captured = log)
            .Returns(Task.CompletedTask);
        var service = new ErrorLogService(repo.Object, NullLogger<ErrorLogService>.Instance);
        var exception = new InvalidOperationException("Provider is down");

        var before = DateTime.UtcNow;
        await service.LogAsync("/Chat/Send", exception, "user-1", "trace-1");

        Assert.NotNull(captured);
        Assert.Equal("/Chat/Send", captured!.Source);
        Assert.Equal("user-1", captured.UserId);
        Assert.Equal("trace-1", captured.TraceId);
        Assert.Equal(typeof(InvalidOperationException).FullName, captured.ExceptionType);
        Assert.Equal("Provider is down", captured.Message);
        Assert.InRange(captured.TimestampUtc, before, DateTime.UtcNow);
        repo.Verify(r => r.AddAsync(It.IsAny<ErrorLog>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LogAsync_LongMessage_ReturnsTruncatedMessage()
    {
        var repo = new Mock<IErrorLogRepository>();
        ErrorLog? captured = null;
        repo.Setup(r => r.AddAsync(It.IsAny<ErrorLog>(), It.IsAny<CancellationToken>()))
            .Callback<ErrorLog, CancellationToken>((log, _) => captured = log)
            .Returns(Task.CompletedTask);
        var service = new ErrorLogService(repo.Object, NullLogger<ErrorLogService>.Instance);

        await service.LogAsync("/Home/Index", new InvalidOperationException(new string('x', 5000)), null, null);

        Assert.NotNull(captured);
        Assert.Equal(4000, captured!.Message.Length);
    }

    [Fact]
    public async Task LogAsync_RepositoryThrows_ReturnsWithoutThrowing()
    {
        var repo = new Mock<IErrorLogRepository>();
        repo.Setup(r => r.AddAsync(It.IsAny<ErrorLog>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database unreachable"));
        var service = new ErrorLogService(repo.Object, NullLogger<ErrorLogService>.Instance);

        // Best-effort: a logging failure must never mask the original exception.
        await service.LogAsync("/Chat/Send", new HttpRequestException("Ollama down"), "user-1", "trace-1");
    }

    [Fact]
    public async Task GetRecentAsync_WithFilters_ReturnsRepositoryResults()
    {
        var since = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var rows = new List<ErrorLog> { new() { Id = 1, Source = "/Chat/Send", Message = "boom", TimestampUtc = since } };
        var repo = new Mock<IErrorLogRepository>();
        repo.Setup(r => r.GetRecentReadOnlyAsync(50, "Chat", since, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        var service = new ErrorLogService(repo.Object, NullLogger<ErrorLogService>.Instance);

        var result = await service.GetRecentAsync(50, "Chat", since);

        Assert.Same(rows, result);
        repo.Verify(r => r.GetRecentReadOnlyAsync(50, "Chat", since, It.IsAny<CancellationToken>()), Times.Once);
    }
}