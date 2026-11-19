using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Moq;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Web.Models;
using StudentManagement.Web.Services;

namespace StudentManagement.Tests;

public class ChatIndexModelFactoryTests
{
    [Fact]
    public async Task BuildAsync_Admin_ReturnsUnlimitedModel()
    {
        // Arrange
        var usage = CreateUsageService(new ChatQuotaStatus(0, 20000, true, DateTime.UtcNow.Date.AddDays(1)));
        var factory = CreateFactory(usage: usage);
        var user = CreatePrincipal("admin-1", AppRoles.Admin);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.True(model.ChatEnabled);
        Assert.True(model.IsAdmin);
        Assert.True(model.Unlimited);
        Assert.Equal(AppRoles.Admin, model.Role);
        usage.Verify(u => u.GetQuotaStatusAsync("admin-1", true, 20000, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildAsync_NonAdmin_ReturnsQuotaValuesFromUsageService()
    {
        // Arrange
        var usage = CreateUsageService(new ChatQuotaStatus(300, 20000, false, DateTime.UtcNow.Date.AddDays(1)));
        var factory = CreateFactory(usage: usage);
        var user = CreatePrincipal("user-1", AppRoles.Student);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.False(model.IsAdmin);
        Assert.False(model.Unlimited);
        Assert.Equal(300, model.UsedToday);
        Assert.Equal(20000, model.DailyLimit);
        usage.Verify(u => u.GetQuotaStatusAsync("user-1", false, 20000, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildAsync_CustomDailyLimit_ReturnsLimitFromSettings()
    {
        // Arrange
        var usage = CreateUsageService(new ChatQuotaStatus(0, 5000, false, DateTime.UtcNow.Date.AddDays(1)));
        var factory = CreateFactory(settings: new ChatSettings { DailyTokenLimit = 5000 }, usage: usage);
        var user = CreatePrincipal("user-1", AppRoles.User);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.Equal(5000, model.DailyLimit);
        usage.Verify(u => u.GetQuotaStatusAsync("user-1", false, 5000, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuildAsync_ToolsDisabled_ReturnsDocumentationOnlyHint()
    {
        // Arrange
        var factory = CreateFactory(settings: new ChatSettings { IncludeTools = false });
        var user = CreatePrincipal("admin-1", AppRoles.Admin);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.False(model.ToolsEnabled);
        Assert.Contains("documentation only", model.CapabilityHint, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AppRoles.Admin)]
    [InlineData(AppRoles.Teacher)]
    public async Task BuildAsync_StaffRoleWithToolsEnabled_ReturnsLiveDataHint(string role)
    {
        // Arrange
        var factory = CreateFactory(settings: new ChatSettings { IncludeTools = true });
        var user = CreatePrincipal("staff-1", role);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.True(model.ToolsEnabled);
        Assert.Contains("students, courses", model.CapabilityHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_LinkedStudentWithToolsEnabled_ReturnsOwnDataHint()
    {
        // Arrange
        var userManager = CreateUserManager(new ApplicationUser { Id = "user-1", StudentId = 5 });
        var factory = CreateFactory(settings: new ChatSettings { IncludeTools = true }, userManager: userManager);
        var user = CreatePrincipal("user-1", AppRoles.Student);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.True(model.HasStudentLink);
        Assert.Equal(AppRoles.Student, model.Role);
        Assert.Contains("your own", model.CapabilityHint, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(AppRoles.Student)]
    [InlineData(AppRoles.User)]
    public async Task BuildAsync_UnlinkedUserWithToolsEnabled_ReturnsLinkAccountHint(string role)
    {
        // Arrange
        var userManager = CreateUserManager(new ApplicationUser { Id = "user-1", StudentId = null });
        var factory = CreateFactory(settings: new ChatSettings { IncludeTools = true }, userManager: userManager);
        var user = CreatePrincipal("user-1", role);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.False(model.HasStudentLink);
        Assert.Contains("link", model.CapabilityHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildAsync_NoKnownRoles_ReturnsUserRoleFallback()
    {
        // Arrange
        var factory = CreateFactory();
        var user = CreatePrincipal("user-1");

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.Equal(AppRoles.User, model.Role);
        Assert.False(model.IsAdmin);
    }

    [Fact]
    public async Task BuildAsync_MultipleRoles_ReturnsFirstRoleInAppRolesOrder()
    {
        // Arrange — claims arrive Student-first, but AppRoles.All orders Admin before Student.
        var factory = CreateFactory();
        var user = CreatePrincipal("user-1", AppRoles.Student, AppRoles.Admin);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.Equal(AppRoles.Admin, model.Role);
        Assert.True(model.IsAdmin);
    }

    [Fact]
    public async Task BuildAsync_ChatDisabled_ReturnsChatEnabledFalse()
    {
        // Arrange
        var factory = CreateFactory(settings: new ChatSettings { ChatEnabled = false });
        var user = CreatePrincipal("user-1", AppRoles.User);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.False(model.ChatEnabled);
    }

    [Fact]
    public async Task BuildAsync_NoUserIdClaim_ReturnsZeroUsageWithoutQuotaCall()
    {
        // Arrange
        var usage = CreateUsageService(new ChatQuotaStatus(300, 20000, false, DateTime.UtcNow.Date.AddDays(1)));
        var factory = CreateFactory(usage: usage);
        var user = CreatePrincipal(null, AppRoles.User);

        // Act
        var model = await factory.BuildAsync(user);

        // Assert
        Assert.Equal(0, model.UsedToday);
        Assert.False(model.Unlimited);
        usage.Verify(
            u => u.GetQuotaStatusAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static ChatIndexModelFactory CreateFactory(
        ChatSettings? settings = null,
        Mock<IChatUsageService>? usage = null,
        Mock<UserManager<ApplicationUser>>? userManager = null)
    {
        var appSettings = new Mock<IAppSettingsService>();
        appSettings
            .Setup(s => s.GetChatSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings ?? new ChatSettings { ChatEnabled = true });

        return new ChatIndexModelFactory(
            appSettings.Object,
            (usage ?? CreateUsageService(new ChatQuotaStatus(0, 20000, false, DateTime.UtcNow.Date.AddDays(1)))).Object,
            (userManager ?? CreateUserManager()).Object);
    }

    private static Mock<IChatUsageService> CreateUsageService(ChatQuotaStatus status)
    {
        var usage = new Mock<IChatUsageService>();
        usage
            .Setup(u => u.GetQuotaStatusAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        return usage;
    }

    private static Mock<UserManager<ApplicationUser>> CreateUserManager(ApplicationUser? user = null)
    {
        var manager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        manager
            .Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(user);
        return manager;
    }

    private static ClaimsPrincipal CreatePrincipal(string? userId, params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
