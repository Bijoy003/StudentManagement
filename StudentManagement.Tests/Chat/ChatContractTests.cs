using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Tests;

// End-to-end contract between ChatService (tool declaration) and Microsoft.Extensions.AI's
// FunctionInvokingChatClient (tool execution). Uses the real ChatTools and a scripted inner
// IChatClient, so the tool-call round trip is exercised without a live provider.
public class ChatContractTests
{
    [Fact]
    public async Task GetReplyAsync_WhenModelRequestsTool_ReturnsFinalAssistantReply()
    {
        var pipeline = CreatePipeline();

        var reply = await pipeline.Service.GetReplyAsync([new ChatMessage(ChatRole.User, "How many students?")], AppRoles.All);

        Assert.Equal("There are 2 students.", reply);
        pipeline.Students.Verify(service => service.GetStudentsAsync(), Times.Once);
    }

    [Fact]
    public async Task GetReplyAsync_WhenModelRequestsTool_ReturnsTwoProviderRequests()
    {
        var pipeline = CreatePipeline();

        await pipeline.Service.GetReplyAsync([new ChatMessage(ChatRole.User, "How many students?")], AppRoles.All);

        Assert.Equal(2, pipeline.Inner.Requests.Count);
        Assert.Equal(2, pipeline.Inner.Requests[0].Count);
        Assert.Equal(4, pipeline.Inner.Requests[1].Count);
    }

    [Fact]
    public async Task GetReplyAsync_WhenModelRequestsTool_ReturnsFunctionResultWithStudentData()
    {
        var pipeline = CreatePipeline();

        await pipeline.Service.GetReplyAsync([new ChatMessage(ChatRole.User, "How many students?")], AppRoles.All);

        var secondRequest = pipeline.Inner.Requests[1];
        var call = Assert.Single(secondRequest.SelectMany(m => m.Contents).OfType<FunctionCallContent>());
        var result = Assert.Single(secondRequest.SelectMany(m => m.Contents).OfType<FunctionResultContent>());

        Assert.Equal("ListStudents", call.Name);
        Assert.Equal(call.CallId, result.CallId);
        Assert.Contains("Ada", result.Result?.ToString());
        Assert.Contains("Grace", result.Result?.ToString());
    }

    [Fact]
    public async Task GetReplyAsync_WhenToolCallCompletes_ReturnsDeclaredListStudentsTool()
    {
        var pipeline = CreatePipeline();

        await pipeline.Service.GetReplyAsync([new ChatMessage(ChatRole.User, "How many students?")], AppRoles.All);

        var tools = pipeline.Inner.Options[0]?.Tools;
        Assert.NotNull(tools);
        Assert.Contains(tools!, tool => tool.Name == "ListStudents");
    }

    private static Pipeline CreatePipeline()
    {
        var students = new Mock<IStudentService>();
        students
            .Setup(service => service.GetStudentsAsync())
            .ReturnsAsync(
            [
                new Student { Id = 1, Name = "Ada", Email = "ada@example.com" },
                new Student { Id = 2, Name = "Grace", Email = "grace@example.com" }
            ]);

        var tools = new ChatTools(
            students.Object,
            Mock.Of<ICourseService>(),
            Mock.Of<IEnrollmentService>());

        var knowledge = new Mock<IAppKnowledgeService>();
        knowledge
            .Setup(service => service.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(string.Empty);

        var inner = new RecordingChatClient();
        Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse> responder = (messages, _) =>
        {
            var hasToolResult = messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionResultContent>()
                .Any();

            return hasToolResult
                ? new ChatResponse(new ChatMessage(ChatRole.Assistant, "There are 2 students."))
                : new ChatResponse(new ChatMessage(
                    ChatRole.Assistant,
                    [new FunctionCallContent("call-1", "ListStudents")]));
        };

        inner.Enqueue(responder);
        inner.Enqueue(responder);

        var invoking = new FunctionInvokingChatClient(inner);

        var service = new ChatService(
            invoking,
            knowledge.Object,
            tools,
            Mock.Of<IMcpToolProvider>(),
            Options.Create(new ChatFeatureOptions { IncludeTools = true }),
            Options.Create(new McpOptions { Enabled = false }),
            NullLogger<ChatService>.Instance);

        return new Pipeline(service, inner, students);
    }

    private sealed record Pipeline(
        ChatService Service,
        RecordingChatClient Inner,
        Mock<IStudentService> Students);
}
