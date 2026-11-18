using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Web.Controllers;
using StudentManagement.Web.Models;

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
        var controller = CreateController(service, new TxtDocumentParser());
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
        var controller = CreateController(service, new TxtDocumentParser());

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
        var controller = CreateController(service, new TxtDocumentParser());

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
            .Setup(s => s.GetReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
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
            .Setup(s => s.GetReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyCollection<string>, CancellationToken>((_, _, token) => observed = token)
            .ReturnsAsync("ok");
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
            .Setup(s => s.GetReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyCollection<string>, CancellationToken>((_, roles, _) => observed = roles)
            .ReturnsAsync("ok");

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
            .Setup(s => s.GetReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyCollection<string>, CancellationToken>((_, roles, _) => observed = roles)
            .ReturnsAsync("ok");

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

    private static ChatController CreateController(Mock<IChatService>? service = null, params IDocumentParser[] parsers)
    {
        IDocumentParser[] parserList = parsers.Length == 0
            ? new IDocumentParser[] { new TxtDocumentParser() }
            : parsers;

        return new ChatController(
            (service ?? new Mock<IChatService>()).Object,
            parserList,
            NullLogger<ChatController>.Instance)
        {
            ControllerContext = CreateContext(AppRoles.Admin)
        };
    }

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
            .Setup(s => s.GetReplyAsync(It.IsAny<IReadOnlyList<ChatMessage>>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ChatMessage>, IReadOnlyCollection<string>, CancellationToken>((history, _, _) => onCall?.Invoke(history))
            .ReturnsAsync(reply);
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
