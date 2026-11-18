using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

public class ChatUsageServiceTests
{
    [Fact]
    public async Task GetUsedTodayAsync_WithUsage_ReturnsRepositoryTotalForUtcToday()
    {
        var repo = new Mock<IChatUsageRepository>();
        repo.Setup(r => r.GetTotalTokensForUserOnDateReadOnlyAsync("user-1", DateTime.UtcNow.Date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1234);
        var service = new ChatUsageService(repo.Object, NullLogger<ChatUsageService>.Instance);

        var used = await service.GetUsedTodayAsync("user-1");

        Assert.Equal(1234, used);
        repo.Verify(r => r.GetTotalTokensForUserOnDateReadOnlyAsync("user-1", DateTime.UtcNow.Date, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordAsync_Usage_ReturnsPersistedRowWithTotals()
    {
        var repo = new Mock<IChatUsageRepository>();
        ChatUsageLog? captured = null;
        repo.Setup(r => r.AddAsync(It.IsAny<ChatUsageLog>(), It.IsAny<CancellationToken>()))
            .Callback<ChatUsageLog, CancellationToken>((log, _) => captured = log)
            .Returns(Task.CompletedTask);
        var service = new ChatUsageService(repo.Object, NullLogger<ChatUsageService>.Instance);

        var before = DateTime.UtcNow;
        await service.RecordAsync("user-1", 100, 50, "qwen2.5:3b-instruct", 3200);

        Assert.NotNull(captured);
        Assert.Equal("user-1", captured!.UserId);
        Assert.Equal(DateTime.UtcNow.Date, captured.UsageDateUtc);
        Assert.Equal(100, captured.InputTokens);
        Assert.Equal(50, captured.OutputTokens);
        Assert.Equal(150, captured.TotalTokens);
        Assert.Equal("qwen2.5:3b-instruct", captured.Model);
        Assert.Equal(3200, captured.ElapsedMs);
        Assert.InRange(captured.CreatedAtUtc, before, DateTime.UtcNow);
    }

    [Fact]
    public async Task GetQuotaStatusAsync_Admin_ReturnsUnlimitedStatus()
    {
        var repo = new Mock<IChatUsageRepository>();
        var service = new ChatUsageService(repo.Object, NullLogger<ChatUsageService>.Instance);

        var status = await service.GetQuotaStatusAsync("admin-1", isAdmin: true, dailyLimit: 100);

        Assert.True(status.IsUnlimited);
        Assert.False(status.Exceeded);
        repo.Verify(r => r.GetTotalTokensForUserOnDateReadOnlyAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetQuotaStatusAsync_NonAdminUnderLimit_ReturnsRemaining()
    {
        var repo = new Mock<IChatUsageRepository>();
        repo.Setup(r => r.GetTotalTokensForUserOnDateReadOnlyAsync("user-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(300);
        var service = new ChatUsageService(repo.Object, NullLogger<ChatUsageService>.Instance);

        var status = await service.GetQuotaStatusAsync("user-1", isAdmin: false, dailyLimit: 1000);

        Assert.False(status.IsUnlimited);
        Assert.False(status.Exceeded);
        Assert.Equal(300, status.UsedToday);
        Assert.Equal(700, status.Remaining);
    }

    [Fact]
    public async Task GetQuotaStatusAsync_NonAdminAtLimit_ReturnsExceeded()
    {
        var repo = new Mock<IChatUsageRepository>();
        repo.Setup(r => r.GetTotalTokensForUserOnDateReadOnlyAsync("user-1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1000);
        var service = new ChatUsageService(repo.Object, NullLogger<ChatUsageService>.Instance);

        var status = await service.GetQuotaStatusAsync("user-1", isAdmin: false, dailyLimit: 1000);

        Assert.True(status.Exceeded);
        Assert.Equal(0, status.Remaining);
    }

    [Fact]
    public async Task GetQuotaStatusAsync_AnyCaller_ReturnsNextUtcMidnightReset()
    {
        var service = new ChatUsageService(Mock.Of<IChatUsageRepository>(), NullLogger<ChatUsageService>.Instance);

        var status = await service.GetQuotaStatusAsync("user-1", isAdmin: false, dailyLimit: 1000);

        Assert.Equal(DateTime.UtcNow.Date.AddDays(1), status.ResetsAtUtc);
    }
}