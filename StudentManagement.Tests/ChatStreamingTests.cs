using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;

namespace StudentManagement.Tests;

public class ChatStreamingTests
{
    private static readonly string[] AdminRoles = [AppRoles.Admin];

    [Fact]
    public async Task StreamReplyAsync_TextUpdates_ReturnsDeltasAndDone()
    {
        var client = CreateStreamingClient(
            new ChatResponseUpdate(ChatRole.Assistant, "Hello"),
            new ChatResponseUpdate(ChatRole.Assistant, " world"));
        var service = CreateService(client.Object);

        var events = await CollectAsync(service.StreamReplyAsync([new ChatMessage(ChatRole.User, "Hi")], AdminRoles));

        var deltas = events.OfType<ChatDeltaEvent>().Select(d => d.Text).ToList();
        Assert.Equal(new[] { "Hello", " world" }, deltas);
        var done = Assert.IsType<ChatDoneEvent>(events[^1]);
        Assert.True(done.ElapsedMs >= 0);
        Assert.True(done.InputTokens > 0);
        Assert.True(done.OutputTokens > 0);
        Assert.Contains(events.OfType<ChatStatusEvent>(), s => s.Status.Contains("Generating"));
    }

    [Fact]
    public async Task StreamReplyAsync_FunctionCall_ReturnsToolStatus()
    {
        var callUpdate = new ChatResponseUpdate();
        callUpdate.Contents.Add(new FunctionCallContent("call-1", "ListStudents"));
        var client = CreateStreamingClient(callUpdate, new ChatResponseUpdate(ChatRole.Assistant, "There are 2 students."));
        var service = CreateService(client.Object, new ChatSettings { IncludeTools = true });

        var events = await CollectAsync(service.StreamReplyAsync([new ChatMessage(ChatRole.User, "How many students?")], AdminRoles));

        Assert.Contains(events.OfType<ChatStatusEvent>(), s => s.Status.Contains("ListStudents"));
        Assert.Equal("There are 2 students.", string.Concat(events.OfType<ChatDeltaEvent>().Select(d => d.Text)));
    }

    [Fact]
    public async Task StreamReplyAsync_RagEnabled_ReturnsSearchingStatusFirst()
    {
        var client = CreateStreamingClient(new ChatResponseUpdate(ChatRole.Assistant, "Answer."));
        var knowledge = new Mock<IAppKnowledgeService>();
        knowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync("CONTEXT");
        var service = CreateService(client.Object, new ChatSettings { IncludeRag = true }, knowledge.Object);

        var events = await CollectAsync(service.StreamReplyAsync([new ChatMessage(ChatRole.User, "How do I enroll?")], AdminRoles));

        var first = Assert.IsType<ChatStatusEvent>(events[0]);
        Assert.Contains("Searching", first.Status);
        knowledge.Verify(k => k.SearchRelevantChunks("How do I enroll?", It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task StreamReplyAsync_UsageContent_ReturnsReportedUsage()
    {
        var usageUpdate = new ChatResponseUpdate();
        usageUpdate.Contents.Add(new UsageContent(new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 }));
        var client = CreateStreamingClient(new ChatResponseUpdate(ChatRole.Assistant, "Counted."), usageUpdate);
        var service = CreateService(client.Object);

        var events = await CollectAsync(service.StreamReplyAsync([new ChatMessage(ChatRole.User, "Hi")], AdminRoles));

        var done = Assert.IsType<ChatDoneEvent>(events[^1]);
        Assert.Equal(100, done.InputTokens);
        Assert.Equal(50, done.OutputTokens);
    }

    [Fact]
    public async Task StreamReplyAsync_LinkedStudent_ReturnsSelfTools()
    {
        ChatOptions? captured = null;
        var client = new Mock<IChatClient>();
        client
            .Setup(c => c.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, options, _) => captured = options)
            .Returns(ToUpdates(new ChatResponseUpdate(ChatRole.Assistant, "Your courses.")));
        var service = CreateService(client.Object, new ChatSettings { IncludeTools = true });

        var events = await CollectAsync(service.StreamReplyAsync(
            [new ChatMessage(ChatRole.User, "My courses?")],
            [AppRoles.Student],
            linkedStudentId: 5));

        Assert.NotNull(captured?.Tools);
        Assert.Contains(captured!.Tools!, t => t.Name == "GetMyCourses");
        Assert.DoesNotContain(captured.Tools!, t => t.Name == "ListStudents");
        Assert.IsType<ChatDoneEvent>(events[^1]);
    }

    private static Mock<IChatClient> CreateStreamingClient(params ChatResponseUpdate[] updates)
    {
        var client = new Mock<IChatClient>();
        client
            .Setup(c => c.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(ToUpdates(updates));
        return client;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToUpdates(params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            yield return update;
            await Task.Yield();
        }
    }

    private static async Task<List<ChatEvent>> CollectAsync(IAsyncEnumerable<ChatEvent> events)
    {
        var collected = new List<ChatEvent>();
        await foreach (var chatEvent in events)
        {
            collected.Add(chatEvent);
        }
        return collected;
    }

    private static ChatService CreateService(
        IChatClient client,
        ChatSettings? settings = null,
        IAppKnowledgeService? knowledge = null)
    {
        var appSettings = new Mock<IAppSettingsService>();
        appSettings.Setup(s => s.GetChatSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings ?? new ChatSettings());

        return new ChatService(
            client,
            knowledge ?? Mock.Of<IAppKnowledgeService>(),
            new ChatTools(Mock.Of<IStudentService>(), Mock.Of<ICourseService>(), Mock.Of<IEnrollmentService>()),
            Mock.Of<IMcpToolProvider>(),
            appSettings.Object,
            Mock.Of<ILogger<ChatService>>());
    }
}