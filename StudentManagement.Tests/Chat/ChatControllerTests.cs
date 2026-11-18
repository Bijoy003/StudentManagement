using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Web.Controllers;
using StudentManagement.Web.Models;
using ChatOptions = StudentManagement.Configuration.ChatOptions;

namespace StudentManagement.Tests;

public class ChatControllerTests
{
    [Fact]
    public async Task Send_NullRequest_Returns400WithError()
    {
        var controller = CreateController();

        var result = await controller.Send(null, CancellationToken.None);

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result).StatusCode);
        Assert.Equal("At least one message is required.", ReadError(result));
    }

    [Fact]
    public async Task Send_EmptyMessages_Returns400WithError()
    {
        var controller = CreateController();

        var result = await controller.Send(new ChatRequest { Messages = [] }, CancellationToken.None);

        Assert.Equal("At least one message is required.", ReadError(result));
    }

    [Fact]
    public async Task Send_LastMessageBlankTextAndNoFile_Returns400WithError()
    {
        var controller = CreateController();

        var result = await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "   " }] },
            CancellationToken.None);

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result).StatusCode);
        Assert.Equal("Message text or file is required.", ReadError(result));
    }

    [Fact]
    public async Task Send_NullMessageEntry_Returns400WithError()
    {
        var controller = CreateController();

        var result = await controller.Send(
            new ChatRequest { Messages = [null!] },
            CancellationToken.None);

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result).StatusCode);
        Assert.Equal("Each message is required.", ReadError(result));
    }

    [Fact]
    public async Task Send_ValidText_ReturnsJsonReply()
    {
        var service = CreateChatService("Hello back.");
        var controller = CreateController(service);

        var result = await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        Assert.Equal("Hello back.", ReadReply(result));
    }

    [Fact]
    public async Task Send_History_ReturnsRolesMappedInOrder()
    {
        var captured = new List<ChatMessage>();
        var service = CreateChatService("ok", history => captured.AddRange(history));
        var controller = CreateController(service);

        await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto { Role = "user", Text = "one" },
                    new ChatMessageDto { Role = "assistant", Text = "two" },
                    new ChatMessageDto { Role = "system", Text = "three" },
                    new ChatMessageDto { Role = "other", Text = "four" }
                ]
            },
            CancellationToken.None);

        Assert.Equal(
            new[] { ChatRole.User, ChatRole.Assistant, ChatRole.System, ChatRole.User },
            captured.Select(m => m.Role));
        Assert.Equal(new[] { "one", "two", "three", "four" }, captured.Select(m => m.Text));
    }

    [Fact]
    public async Task Send_NullRole_ReturnsUserRole()
    {
        IReadOnlyList<ChatMessage>? captured = null;
        var service = CreateChatService("ok", history => captured = history);
        var controller = CreateController(service);

        await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = null!, Text = "hello" }] },
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal(ChatRole.User, Assert.Single(captured!).Role);
    }

    [Fact]
    public async Task Send_TextFileAttachment_ReturnsParsedFileTextInHistory()
    {
        IReadOnlyList<ChatMessage>? captured = null;
        var service = CreateChatService("ok", history => captured = history);
        var controller = CreateController(service, parsers: new TxtDocumentParser());
        var base64 = Base64("file body");

        await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto { Role = "user", FileName = "notes.txt", FileData = base64 }
                ]
            },
            CancellationToken.None);

        var message = Assert.Single(captured!);
        Assert.Equal("[Attached file: notes.txt]\n\nfile body", message.Text);
    }

    [Fact]
    public async Task Send_TextFileWithMessage_ReturnsMessageAndFileTextInHistory()
    {
        IReadOnlyList<ChatMessage>? captured = null;
        var service = CreateChatService("ok", history => captured = history);
        var controller = CreateController(service, parsers: new TxtDocumentParser());

        await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto
                    {
                        Role = "user",
                        Text = "Summarize this",
                        FileName = "notes.txt",
                        FileData = Base64("file body")
                    }
                ]
            },
            CancellationToken.None);

        Assert.Equal("Summarize this\n\n[Attached file: notes.txt]\n\nfile body", Assert.Single(captured!).Text);
    }

    [Fact]
    public async Task Send_UnsupportedAttachment_ReturnsUnsupportedMarkerInHistory()
    {
        IReadOnlyList<ChatMessage>? captured = null;
        var service = CreateChatService("ok", history => captured = history);
        var controller = CreateController(service, parsers: new TxtDocumentParser());

        await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto { Role = "user", FileName = "data.bin", FileData = Base64("binary") }
                ]
            },
            CancellationToken.None);

        Assert.Contains("[Unsupported file type: data.bin]", Assert.Single(captured!).Text);
    }

    [Theory]
    [InlineData("image.png", "image/png")]
    [InlineData("image.jpg", "image/jpeg")]
    [InlineData("image.jpeg", "image/jpeg")]
    [InlineData("image.gif", "image/gif")]
    [InlineData("image.webp", "image/webp")]
    public async Task Send_ImageAttachment_ReturnsDataContentWithExpectedMimeType(string fileName, string expectedMimeType)
    {
        IReadOnlyList<ChatMessage>? captured = null;
        var service = CreateChatService("ok", history => captured = history);
        var controller = CreateController(service);

        await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto { Role = "user", FileName = fileName, FileData = Base64("pixels") }
                ]
            },
            CancellationToken.None);

        var message = Assert.Single(captured!);
        var data = Assert.Single(message.Contents.OfType<DataContent>());
        Assert.Equal(expectedMimeType, data.MediaType);
        Assert.Equal(fileName, data.Name);
        Assert.Equal(Encoding.UTF8.GetBytes("pixels"), data.Data.ToArray());
    }

    [Fact]
    public async Task Send_ImageAttachmentWithText_ReturnsTextAndImageContents()
    {
        IReadOnlyList<ChatMessage>? captured = null;
        var service = CreateChatService("ok", history => captured = history);
        var controller = CreateController(service);

        await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto { Role = "user", Text = "What is this?", FileName = "image.png", FileData = Base64("pixels") }
                ]
            },
            CancellationToken.None);

        var message = Assert.Single(captured!);
        Assert.Contains(message.Contents, content => content is TextContent text && text.Text == "What is this?");
        Assert.Contains(message.Contents, content => content is DataContent);
    }

    [Fact]
    public async Task Send_InvalidBase64Image_Returns400Not500()
    {
        var controller = CreateController();

        var result = await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto { Role = "user", FileName = "image.png", FileData = "***not-base64***" }
                ]
            },
            CancellationToken.None);

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result).StatusCode);
        Assert.Equal("The attached file could not be read.", ReadError(result));
    }

    [Fact]
    public async Task Send_InvalidBase64TextFile_Returns400Not500()
    {
        var controller = CreateController();

        var result = await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto { Role = "user", FileName = "notes.txt", FileData = "***not-base64***" }
                ]
            },
            CancellationToken.None);

        Assert.Equal(400, Assert.IsType<BadRequestObjectResult>(result).StatusCode);
        Assert.Equal("The attached file could not be read.", ReadError(result));
    }

    [Fact]
    public async Task Send_FileWithoutName_ReturnsTextOnly()
    {
        IReadOnlyList<ChatMessage>? captured = null;
        var service = CreateChatService("ok", history => captured = history);
        var controller = CreateController(service);

        await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto { Role = "user", Text = "hello", FileData = Base64("ignored") }
                ]
            },
            CancellationToken.None);

        Assert.Equal("hello", Assert.Single(captured!).Text);
    }

    [Fact]
    public async Task Send_FileWithoutData_ReturnsTextOnly()
    {
        IReadOnlyList<ChatMessage>? captured = null;
        var service = CreateChatService("ok", history => captured = history);
        var controller = CreateController(service);

        await controller.Send(
            new ChatRequest
            {
                Messages =
                [
                    new ChatMessageDto { Role = "user", Text = "hello", FileName = "notes.txt" }
                ]
            },
            CancellationToken.None);

        Assert.Equal("hello", Assert.Single(captured!).Text);
    }

    [Fact]
    public async Task Send_ChatServiceThrows_Returns503WithFriendlyMessage()
    {
        var service = new Mock<IChatService>();
        service
            .Setup(s => s.GetReplyWithUsageAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(new HttpRequestException("Ollama is down at http://localhost:11434"));
        var controller = CreateController(service);

        var result = await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Contains("currently unavailable", ReadError(result));
        Assert.DoesNotContain("11434", ReadError(result));
    }

    [Fact]
    public async Task Send_CancellationToken_ReturnsForwardedToken()
    {
        CancellationToken observed = default;
        var service = new Mock<IChatService>();
        service
            .Setup(s => s.GetReplyWithUsageAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyCollection<string>, CancellationToken, int?>((_, _, token, _) => observed = token)
            .ReturnsAsync(new ChatReplyResult("ok", 1, 1, 1, "test-model"));
        var controller = CreateController(service);

        using var cts = new CancellationTokenSource();
        await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    [Fact]
    public async Task Send_WhenUserIsStudent_ReturnsStudentRoleToChatService()
    {
        IReadOnlyCollection<string>? observed = null;
        var service = new Mock<IChatService>();
        service
            .Setup(s => s.GetReplyWithUsageAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyCollection<string>, CancellationToken, int?>((_, roles, _, _) => observed = roles)
            .ReturnsAsync(new ChatReplyResult("ok", 1, 1, 1, "test-model"));

        var controller = CreateController(service);
        controller.ControllerContext = CreateContext(AppRoles.Student);

        await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        Assert.NotNull(observed);
        Assert.Equal(AppRoles.Student, Assert.Single(observed!));
    }

    [Fact]
    public async Task Send_WhenUserHasNoRoles_ReturnsEmptyRoleListToChatService()
    {
        IReadOnlyCollection<string>? observed = null;
        var service = new Mock<IChatService>();
        service
            .Setup(s => s.GetReplyWithUsageAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyCollection<string>, CancellationToken, int?>((_, roles, _, _) => observed = roles)
            .ReturnsAsync(new ChatReplyResult("ok", 1, 1, 1, "test-model"));

        var controller = CreateController(service);
        controller.ControllerContext = CreateContext();

        await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        Assert.NotNull(observed);
        Assert.Empty(observed!);
    }

    [Fact]
    public void ChatController_WhenInspected_ReturnsAuthorizeAttribute()
    {
        Assert.NotNull(typeof(ChatController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void Send_WhenInspected_ReturnsHttpPostWithAntiForgeryToken()
    {
        var action = typeof(ChatController).GetMethod("Send");
        Assert.NotNull(action);
        Assert.NotNull(action!.GetCustomAttribute<HttpPostAttribute>());
        Assert.NotNull(action.GetCustomAttribute<ValidateAntiForgeryTokenAttribute>());
    }

    [Fact]
    public async Task Send_ChatDisabled_Returns503()
    {
        var service = new Mock<IChatService>();
        var controller = CreateController(service, settings: new ChatSettings { ChatEnabled = false });

        var result = await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
        Assert.Equal("Chat is currently disabled by the administrator.", ReadError(result));
        service.Verify(
            s => s.GetReplyWithUsageAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()),
            Times.Never);
    }

    [Fact]
    public async Task Send_QuotaExceeded_Returns429()
    {
        var service = new Mock<IChatService>();
        var usage = CreateUsageService(new ChatQuotaStatus(20000, 20000, false, DateTime.UtcNow.Date.AddDays(1)));
        var controller = CreateController(service, usage: usage);
        controller.ControllerContext = CreateContextWithUser("user-1", AppRoles.Student);

        var result = await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(429, objectResult.StatusCode);
        Assert.Contains("limit", ReadError(result), StringComparison.OrdinalIgnoreCase);
        service.Verify(
            s => s.GetReplyWithUsageAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()),
            Times.Never);
    }

    [Fact]
    public async Task Send_AdminSuccess_ReturnsReplyAndRecordsUsage()
    {
        var service = CreateChatService("Admin reply.");
        var usage = CreateUsageService(new ChatQuotaStatus(0, 20000, true, DateTime.UtcNow.Date.AddDays(1)));
        var controller = CreateController(service, usage: usage);
        controller.ControllerContext = CreateContextWithUser("admin-1", AppRoles.Admin);

        var result = await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        Assert.Equal("Admin reply.", ReadReply(result));
        usage.Verify(u => u.RecordAsync("admin-1", 10, 5, "test-model", 42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_NonAdminSuccess_ReturnsQuotaInJson()
    {
        var service = CreateChatService("Student reply.");
        var usage = CreateUsageService(new ChatQuotaStatus(300, 20000, false, DateTime.UtcNow.Date.AddDays(1)));
        var controller = CreateController(service, usage: usage);
        controller.ControllerContext = CreateContextWithUser("user-1", AppRoles.Student);

        var result = await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        var value = Assert.IsType<JsonResult>(result).Value;
        Assert.Equal(42L, ReadProperty<long>(value!, "elapsedMs"));
        var quota = value!.GetType().GetProperty("quota")!.GetValue(value);
        Assert.NotNull(quota);
        Assert.Equal(315, ReadProperty<int>(quota!, "used"));
        Assert.Equal(20000, ReadProperty<int>(quota!, "limit"));
        Assert.Equal(19685, ReadProperty<int>(quota!, "remaining"));
        usage.Verify(u => u.RecordAsync("user-1", 10, 5, "test-model", 42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Index_NonAdmin_ReturnsQuotaViewModel()
    {
        var usage = CreateUsageService(new ChatQuotaStatus(300, 20000, false, DateTime.UtcNow.Date.AddDays(1)));
        var controller = CreateController(usage: usage);
        controller.ControllerContext = CreateContextWithUser("user-1", AppRoles.Student);

        var result = await controller.Index();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ChatIndexViewModel>(view.Model);
        Assert.False(model.Unlimited);
        Assert.Equal(300, model.UsedToday);
        Assert.Equal(20000, model.DailyLimit);
        Assert.True(model.ChatEnabled);
    }

    [Fact]
    public async Task Send_ProviderFailure_Returns503AndLogsError()
    {
        var service = new Mock<IChatService>();
        var failure = new HttpRequestException("Ollama is down");
        service
            .Setup(s => s.GetReplyWithUsageAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .ThrowsAsync(failure);
        var errorLog = new Mock<IErrorLogService>();
        errorLog
            .Setup(s => s.LogAsync(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = CreateController(service, errorLog: errorLog);

        var result = await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        errorLog.Verify(s => s.LogAsync(
            "Chat/Send",
            failure,
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Send_LinkedStudent_ReturnsLinkedStudentIdToService()
    {
        int? observedStudentId = null;
        var service = new Mock<IChatService>();
        service
            .Setup(s => s.GetReplyWithUsageAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyCollection<string>, CancellationToken, int?>((_, _, _, studentId) => observedStudentId = studentId)
            .ReturnsAsync(new ChatReplyResult("ok", 1, 1, 1, "test-model"));
        var userManager = CreateUserManager(new ApplicationUser { Id = "user-1", StudentId = 5 });
        var controller = CreateController(service, userManager: userManager);
        controller.ControllerContext = CreateContextWithUser("user-1", AppRoles.Student);

        await controller.Send(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "My grades?" }] },
            CancellationToken.None);

        Assert.Equal(5, observedStudentId);
    }

    [Fact]
    public async Task Index_LinkedStudent_ReturnsSelfServiceHint()
    {
        var userManager = CreateUserManager(new ApplicationUser { Id = "user-1", StudentId = 5 });
        var controller = CreateController(settings: new ChatSettings { IncludeTools = true }, userManager: userManager);
        controller.ControllerContext = CreateContextWithUser("user-1", AppRoles.Student);

        var result = await controller.Index();

        var model = Assert.IsType<ChatIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.True(model.HasStudentLink);
        Assert.Equal(AppRoles.Student, model.Role);
        Assert.Contains("your own", model.CapabilityHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Index_UnlinkedStudent_ReturnsLinkAccountHint()
    {
        var userManager = CreateUserManager(new ApplicationUser { Id = "user-1", StudentId = null });
        var controller = CreateController(settings: new ChatSettings { IncludeTools = true }, userManager: userManager);
        controller.ControllerContext = CreateContextWithUser("user-1", AppRoles.Student);

        var result = await controller.Index();

        var model = Assert.IsType<ChatIndexViewModel>(Assert.IsType<ViewResult>(result).Model);
        Assert.False(model.HasStudentLink);
        Assert.Contains("link", model.CapabilityHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendStream_ValidRequest_ReturnsStatusDeltaDoneFrames()
    {
        var service = new Mock<IChatService>();
        service
            .Setup(s => s.StreamReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Returns(StreamEvents(
                new ChatStatusEvent("Generating reply…"),
                new ChatDeltaEvent("Hello"),
                new ChatDoneEvent(42, 10, 5)));
        var usage = CreateUsageService(new ChatQuotaStatus(0, 20000, true, DateTime.UtcNow.Date.AddDays(1)));
        var controller = CreateController(service, usage: usage);
        controller.ControllerContext = CreateContextWithUser("admin-1", AppRoles.Admin);
        controller.HttpContext.Response.Body = new MemoryStream();

        var result = await controller.SendStream(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        Assert.IsType<EmptyResult>(result);
        Assert.Equal("text/event-stream", controller.HttpContext.Response.ContentType);
        var frames = ReadSseFrames(controller.HttpContext);
        Assert.Equal(3, frames.Count);
        Assert.Equal("status", frames[0].RootElement.GetProperty("type").GetString());
        Assert.Equal("delta", frames[1].RootElement.GetProperty("type").GetString());
        Assert.Equal("Hello", frames[1].RootElement.GetProperty("text").GetString());
        var done = frames[2].RootElement;
        Assert.Equal("done", done.GetProperty("type").GetString());
        Assert.Equal(42, done.GetProperty("elapsedMs").GetInt64());
        Assert.Equal(15, done.GetProperty("quota").GetProperty("used").GetInt32());
        usage.Verify(u => u.RecordAsync("admin-1", 10, 5, "test-model", 42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendStream_QuotaExceeded_Returns429()
    {
        var service = new Mock<IChatService>();
        var usage = CreateUsageService(new ChatQuotaStatus(20000, 20000, false, DateTime.UtcNow.Date.AddDays(1)));
        var controller = CreateController(service, usage: usage);
        controller.ControllerContext = CreateContextWithUser("user-1", AppRoles.Student);

        var result = await controller.SendStream(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        Assert.Equal(429, Assert.IsType<ObjectResult>(result).StatusCode);
        service.Verify(
            s => s.StreamReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()),
            Times.Never);
    }

    [Fact]
    public async Task SendStream_ChatDisabled_Returns503()
    {
        var controller = CreateController(settings: new ChatSettings { ChatEnabled = false });

        var result = await controller.SendStream(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        Assert.Equal(503, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.Equal("Chat is currently disabled by the administrator.", ReadError(result));
    }

    [Fact]
    public async Task SendStream_StreamThrows_ReturnsErrorFrameAndLogs()
    {
        var service = new Mock<IChatService>();
        service
            .Setup(s => s.StreamReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Returns(FailingStream());
        var errorLog = new Mock<IErrorLogService>();
        errorLog
            .Setup(s => s.LogAsync(It.IsAny<string>(), It.IsAny<Exception>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var controller = CreateController(service, errorLog: errorLog);
        controller.ControllerContext = CreateContextWithUser("user-1", AppRoles.Student);
        controller.HttpContext.Response.Body = new MemoryStream();

        await controller.SendStream(
            new ChatRequest { Messages = [new ChatMessageDto { Role = "user", Text = "Hello" }] },
            CancellationToken.None);

        var frames = ReadSseFrames(controller.HttpContext);
        Assert.Contains(frames, f => f.RootElement.GetProperty("type").GetString() == "error");
        errorLog.Verify(s => s.LogAsync(
            "Chat/SendStream",
            It.IsAny<InvalidOperationException>(),
            "user-1",
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async IAsyncEnumerable<ChatEvent> StreamEvents(params ChatEvent[] events)
    {
        foreach (var chatEvent in events)
        {
            yield return chatEvent;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<ChatEvent> FailingStream()
    {
        yield return new ChatStatusEvent("Generating reply…");
        await Task.Yield();
        throw new InvalidOperationException("Provider down");
    }

    private static List<System.Text.Json.JsonDocument> ReadSseFrames(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        var body = reader.ReadToEnd();
        return body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Where(frame => frame.StartsWith("data: "))
            .Select(frame => System.Text.Json.JsonDocument.Parse(frame["data: ".Length..]))
            .ToList();
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

    private static ChatController CreateController(
        Mock<IChatService>? service = null,
        ChatSettings? settings = null,
        Mock<IErrorLogService>? errorLog = null,
        Mock<IChatUsageService>? usage = null,
        Mock<UserManager<ApplicationUser>>? userManager = null,
        params IDocumentParser[] parsers)
    {
        IDocumentParser[] parserList = parsers.Length == 0
            ? new IDocumentParser[] { new TxtDocumentParser() }
            : parsers;

        var appSettings = new Mock<IAppSettingsService>();
        appSettings
            .Setup(s => s.GetChatSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings ?? new ChatSettings { ChatEnabled = true });

        return new ChatController(
            (service ?? new Mock<IChatService>()).Object,
            parserList,
            appSettings.Object,
            (errorLog ?? new Mock<IErrorLogService>()).Object,
            (usage ?? CreateUsageService(new ChatQuotaStatus(0, 20000, false, DateTime.UtcNow.Date.AddDays(1)))).Object,
            Options.Create(new ChatOptions { Model = "test-model" }),
            (userManager ?? CreateUserManager()).Object,
            NullLogger<ChatController>.Instance)
        {
            ControllerContext = CreateContext(AppRoles.Admin)
        };
    }

    private static Mock<IChatUsageService> CreateUsageService(ChatQuotaStatus status)
    {
        var usage = new Mock<IChatUsageService>();
        usage
            .Setup(u => u.GetQuotaStatusAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);
        usage
            .Setup(u => u.RecordAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return usage;
    }

    private static ControllerContext CreateContextWithUser(string userId, params string[] roles)
    {
        var claims = roles.Select(role => new Claim(ClaimTypes.Role, role)).ToList();
        claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        var identity = new ClaimsIdentity(claims, "TestAuth");

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
    }

    private static T ReadProperty<T>(object value, string name) =>
        (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    private static ControllerContext CreateContext(params string[] roles)
    {
        var identity = new ClaimsIdentity(
            roles.Select(role => new Claim(ClaimTypes.Role, role)),
            "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = principal }
        };
    }

    private static Mock<IChatService> CreateChatService(string reply, Action<IReadOnlyList<ChatMessage>>? onCall = null)
    {
        var service = new Mock<IChatService>();
        service
            .Setup(s => s.GetReplyWithUsageAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyCollection<string>, CancellationToken, int?>((history, _, _, _) => onCall?.Invoke(history))
            .ReturnsAsync(new ChatReplyResult(reply, 10, 5, 42, "test-model"));
        return service;
    }

    private static string Base64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private static string? ReadError(IActionResult result)
    {
        var value = (result as ObjectResult)?.Value;
        return value?.GetType().GetProperty("error")?.GetValue(value) as string;
    }

    private static string? ReadReply(IActionResult result)
    {
        var value = Assert.IsType<JsonResult>(result).Value;
        return value?.GetType().GetProperty("reply")?.GetValue(value) as string;
    }
}
