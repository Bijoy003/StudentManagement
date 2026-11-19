using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Moq;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Web.Models;
using StudentManagement.Web.Services;
using StudentManagement.Web.ViewComponents;

namespace StudentManagement.Tests;

public class ChatWidgetViewComponentTests
{
    [Fact]
    public async Task InvokeAsync_ChatDisabled_ReturnsEmptyContent()
    {
        // Arrange
        var factory = new Mock<IChatIndexModelFactory>();
        var component = CreateComponent(factory, new ChatSettings { ChatEnabled = false }, CreatePrincipal("admin-1", AppRoles.Admin));

        // Act
        var result = await component.InvokeAsync();

        // Assert
        var content = Assert.IsType<ContentViewComponentResult>(result);
        Assert.Equal(string.Empty, content.Content);
        factory.Verify(f => f.BuildAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_AnonymousCaller_ReturnsEmptyContent()
    {
        // Arrange
        var factory = new Mock<IChatIndexModelFactory>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var component = CreateComponent(factory, new ChatSettings { ChatEnabled = true }, anonymous);

        // Act
        var result = await component.InvokeAsync();

        // Assert
        var content = Assert.IsType<ContentViewComponentResult>(result);
        Assert.Equal(string.Empty, content.Content);
        factory.Verify(f => f.BuildAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_ChatDisabledAndAnonymous_ReturnsEmptyContent()
    {
        // Arrange
        var factory = new Mock<IChatIndexModelFactory>();
        var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
        var component = CreateComponent(factory, new ChatSettings { ChatEnabled = false }, anonymous);

        // Act
        var result = await component.InvokeAsync();

        // Assert
        Assert.Equal(string.Empty, Assert.IsType<ContentViewComponentResult>(result).Content);
        factory.Verify(f => f.BuildAsync(It.IsAny<ClaimsPrincipal>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_EnabledAuthenticatedAdmin_ReturnsViewWithAdminModel()
    {
        // Arrange
        var model = new ChatIndexViewModel
        {
            ChatEnabled = true,
            IsAdmin = true,
            Unlimited = true,
            UsedToday = 0,
            DailyLimit = 20000,
            Role = AppRoles.Admin,
            ToolsEnabled = true,
            CapabilityHint = "You can query students, courses, enrollments, counts, and grades."
        };
        var factory = new Mock<IChatIndexModelFactory>();
        factory.Setup(f => f.BuildAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(model);
        var component = CreateComponent(factory, new ChatSettings { ChatEnabled = true }, CreatePrincipal("admin-1", AppRoles.Admin));

        // Act
        var result = await component.InvokeAsync();

        // Assert
        var view = Assert.IsType<ViewViewComponentResult>(result);
        var actual = Assert.IsType<ChatIndexViewModel>(view.ViewData.Model);
        Assert.True(actual.ChatEnabled);
        Assert.True(actual.IsAdmin);
        Assert.True(actual.Unlimited);
        Assert.Equal(AppRoles.Admin, actual.Role);
        Assert.Equal(model.CapabilityHint, actual.CapabilityHint);
    }

    [Fact]
    public async Task InvokeAsync_EnabledAuthenticatedStudent_ReturnsViewWithQuotaModel()
    {
        // Arrange
        var model = new ChatIndexViewModel
        {
            ChatEnabled = true,
            IsAdmin = false,
            Unlimited = false,
            UsedToday = 300,
            DailyLimit = 20000,
            Role = AppRoles.Student,
            HasStudentLink = true,
            ToolsEnabled = true,
            CapabilityHint = "You can ask about your own courses, grades, and profile, plus the course catalog."
        };
        var factory = new Mock<IChatIndexModelFactory>();
        factory.Setup(f => f.BuildAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(model);
        var component = CreateComponent(factory, new ChatSettings { ChatEnabled = true }, CreatePrincipal("user-1", AppRoles.Student));

        // Act
        var result = await component.InvokeAsync();

        // Assert
        var view = Assert.IsType<ViewViewComponentResult>(result);
        var actual = Assert.IsType<ChatIndexViewModel>(view.ViewData.Model);
        Assert.False(actual.Unlimited);
        Assert.Equal(300, actual.UsedToday);
        Assert.Equal(20000, actual.DailyLimit);
        Assert.True(actual.HasStudentLink);
        Assert.Equal(AppRoles.Student, actual.Role);
    }

    [Fact]
    public async Task InvokeAsync_EnabledAuthenticated_ReturnsFactoryModelBuiltForCaller()
    {
        // Arrange
        var principal = CreatePrincipal("user-1", AppRoles.Teacher);
        var factory = new Mock<IChatIndexModelFactory>();
        factory.Setup(f => f.BuildAsync(principal)).ReturnsAsync(new ChatIndexViewModel { Role = AppRoles.Teacher });
        var component = CreateComponent(factory, new ChatSettings { ChatEnabled = true }, principal);

        // Act
        var result = await component.InvokeAsync();

        // Assert
        Assert.IsType<ViewViewComponentResult>(result);
        factory.Verify(f => f.BuildAsync(principal), Times.Once);
    }

    private static ChatWidgetViewComponent CreateComponent(
        Mock<IChatIndexModelFactory> factory,
        ChatSettings settings,
        ClaimsPrincipal user)
    {
        var appSettings = new Mock<IAppSettingsService>();
        appSettings
            .Setup(s => s.GetChatSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);

        var component = new ChatWidgetViewComponent(factory.Object, appSettings.Object);
        component.ViewComponentContext = new ViewComponentContext
        {
            ViewContext = new ViewContext
            {
                HttpContext = new DefaultHttpContext { User = user },
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
            }
        };
        return component;
    }

    private static ClaimsPrincipal CreatePrincipal(string userId, params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
        claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }
}
