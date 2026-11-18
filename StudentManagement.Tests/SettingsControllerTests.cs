using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Web.Controllers;
using StudentManagement.Web.Models;

namespace StudentManagement.Tests;

public class SettingsControllerTests
{
    private const string AdminUserId = "admin-user-id";

    [Fact]
    public async Task Settings_Get_ReturnsViewWithEffectiveSettings()
    {
        // Arrange
        var service = new Mock<IAppSettingsService>();
        service.Setup(s => s.GetChatSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatSettings
            {
                ChatEnabled = false,
                IncludeRag = true,
                IncludeTools = true,
                IncludeFullDocumentation = true,
                MaxRagChunks = 7,
                McpEnabled = true,
                DailyTokenLimit = 1234
            });
        var controller = CreateController(service);

        // Act
        var result = await controller.Settings();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ChatSettingsViewModel>(view.Model);
        Assert.False(model.ChatEnabled);
        Assert.True(model.IncludeRag);
        Assert.True(model.IncludeTools);
        Assert.True(model.IncludeFullDocumentation);
        Assert.Equal(7, model.MaxRagChunks);
        Assert.True(model.McpEnabled);
        Assert.Equal(1234, model.DailyTokenLimit);
    }

    [Fact]
    public async Task SaveSettings_ValidModel_ReturnsRedirectAndSavesWithCallerId()
    {
        // Arrange
        ChatSettings? saved = null;
        string? savedBy = null;
        var service = new Mock<IAppSettingsService>();
        service.Setup(s => s.SaveChatSettingsAsync(It.IsAny<ChatSettings>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<ChatSettings, string?, CancellationToken>((settings, userId, _) =>
            {
                saved = settings;
                savedBy = userId;
            })
            .Returns(Task.CompletedTask);
        var controller = CreateController(service);
        var model = new ChatSettingsViewModel
        {
            ChatEnabled = false,
            IncludeRag = true,
            IncludeTools = false,
            IncludeFullDocumentation = true,
            MaxRagChunks = 9,
            McpEnabled = true,
            DailyTokenLimit = 4321
        };

        // Act
        var result = await controller.SaveSettings(model);

        // Assert — redirect back to the settings page with a success flash
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Settings", redirect.ActionName);
        Assert.Equal("Settings saved.", controller.TempData["Success"]);

        // Assert — the mapped settings and the caller's nameidentifier reached the service
        service.Verify(s => s.SaveChatSettingsAsync(It.IsAny<ChatSettings>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(saved);
        Assert.False(saved!.ChatEnabled);
        Assert.True(saved.IncludeRag);
        Assert.False(saved.IncludeTools);
        Assert.True(saved.IncludeFullDocumentation);
        Assert.Equal(9, saved.MaxRagChunks);
        Assert.True(saved.McpEnabled);
        Assert.Equal(4321, saved.DailyTokenLimit);
        Assert.Equal(AdminUserId, savedBy);
    }

    [Fact]
    public async Task SaveSettings_InvalidModelState_ReturnsSettingsViewWithoutSaving()
    {
        // Arrange
        var service = new Mock<IAppSettingsService>();
        var controller = CreateController(service);
        controller.ModelState.AddModelError("MaxRagChunks", "Must be between 1 and 20.");
        var model = new ChatSettingsViewModel { MaxRagChunks = 99 };

        // Act
        var result = await controller.SaveSettings(model);

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("Settings", view.ViewName);
        Assert.Same(model, view.Model);
        service.Verify(s => s.SaveChatSettingsAsync(It.IsAny<ChatSettings>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void SettingsController_WhenInspected_ReturnsAdminOnlyAuthorizeAttribute()
    {
        // Arrange / Act
        var attribute = typeof(SettingsController).GetCustomAttribute<AuthorizeAttribute>();

        // Assert
        Assert.NotNull(attribute);
        Assert.Equal(Policies.AdminOnly, attribute!.Policy);
    }

    [Fact]
    public void SaveSettings_WhenInspected_ReturnsHttpPostWithAntiForgeryToken()
    {
        // Arrange / Act
        var action = typeof(SettingsController).GetMethod("SaveSettings");

        // Assert
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public void ChatSettingsViewModel_WhenInspected_ReturnsRangeValidation()
    {
        // Arrange / Act
        var chunksRange = typeof(ChatSettingsViewModel)
            .GetProperty(nameof(ChatSettingsViewModel.MaxRagChunks))!
            .GetCustomAttribute<RangeAttribute>();
        var limitRange = typeof(ChatSettingsViewModel)
            .GetProperty(nameof(ChatSettingsViewModel.DailyTokenLimit))!
            .GetCustomAttribute<RangeAttribute>();

        // Assert
        Assert.NotNull(chunksRange);
        Assert.Equal(1, chunksRange!.Minimum);
        Assert.Equal(20, chunksRange.Maximum);
        Assert.NotNull(limitRange);
        Assert.Equal(0, limitRange!.Minimum);
        Assert.Equal(10000000, limitRange.Maximum);
    }

    [Fact]
    public async Task Errors_Get_ReturnsViewWithRecentErrors()
    {
        var errorLog = new Mock<IErrorLogService>();
        var rows = new List<ErrorLog>
        {
            new()
            {
                Id = 7,
                TimestampUtc = new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Utc),
                Source = "/Chat/Send",
                UserId = "user-1",
                ExceptionType = "System.InvalidOperationException",
                Message = "Provider is down",
                StackTrace = "at ChatService.GetReplyAsync()",
                TraceId = "trace-1"
            }
        };
        errorLog
            .Setup(s => s.GetRecentAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        var controller = CreateController(errorLog: errorLog);

        var result = await controller.Errors(7, "Chat");

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ErrorLogListViewModel>(view.Model);
        Assert.Equal(7, model.Days);
        Assert.Equal("Chat", model.Source);
        var entry = Assert.Single(model.Entries);
        Assert.Equal(7, entry.Id);
        Assert.Equal("/Chat/Send", entry.Source);
        Assert.Equal("Provider is down", entry.Message);
        Assert.Equal("trace-1", entry.TraceId);
        errorLog.Verify(s => s.GetRecentAsync(
            200,
            "Chat",
            It.Is<DateTime?>(d => d != null && d.Value <= DateTime.UtcNow.AddDays(-7).AddMinutes(1) && d.Value >= DateTime.UtcNow.AddDays(-7).AddMinutes(-1)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(500, 90)]
    public async Task Errors_DaysOutOfRange_ReturnsClampedDays(int requested, int expected)
    {
        var errorLog = new Mock<IErrorLogService>();
        errorLog
            .Setup(s => s.GetRecentAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErrorLog>());
        var controller = CreateController(errorLog: errorLog);

        var result = await controller.Errors(requested, null);

        var model = Assert.IsType<ErrorLogListViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.Equal(expected, model.Days);
    }

    private static SettingsController CreateController(
        Mock<IAppSettingsService>? service = null,
        Mock<IErrorLogService>? errorLog = null)
    {
        var identity = new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, AdminUserId),
                new Claim(ClaimTypes.Role, AppRoles.Admin)
            },
            "TestAuth");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };

        return new SettingsController(
            (service ?? new Mock<IAppSettingsService>()).Object,
            (errorLog ?? new Mock<IErrorLogService>()).Object,
            NullLogger<SettingsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>())
        };
    }
}
