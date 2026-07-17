using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;

namespace StudentManagement.Tests
{
    public class ChatServiceTests
    {
        [Fact]
        public async Task GetReplyAsync_ReturnsAssistantText()
        {
            var mockChatClient = new Mock<IChatClient>();
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Hello from the assistant.")));

            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.GetDocumentation()).Returns("Application documentation.");
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).Returns(string.Empty);

            var chatTools = new ChatTools(
                Mock.Of<IStudentService>(),
                Mock.Of<ICourseService>(),
                Mock.Of<IEnrollmentService>());

            var features = Options.Create(new ChatFeatureOptions());
            var mcpOptions = Options.Create(new McpOptions());

            var mockMcpTools = new Mock<IMcpToolProvider>();
            mockMcpTools
                .Setup(m => m.GetToolsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            var chatService = new ChatService(
                mockChatClient.Object,
                mockKnowledge.Object,
                chatTools,
                mockMcpTools.Object,
                features,
                mcpOptions);
            var history = new List<ChatMessage> { new(ChatRole.User, "Hi") };

            var reply = await chatService.GetReplyAsync(history);

            Assert.Equal("Hello from the assistant.", reply);
            mockChatClient.Verify(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null && o.Tools != null && o.Tools.Count > 0),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetReplyAsync_IncludesMcpTools_WhenMcpEnabled()
        {
            var mockChatClient = new Mock<IChatClient>();
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Jira reply.")));

            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).Returns(string.Empty);

            var mcpTool = AIFunctionFactory.Create(() => "ok", "jira_search", "Search Jira");

            var mockMcpTools = new Mock<IMcpToolProvider>();
            mockMcpTools
                .Setup(m => m.GetToolsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([mcpTool]);

            var features = Options.Create(new ChatFeatureOptions { IncludeTools = false });
            var mcpOptions = Options.Create(new McpOptions { Enabled = true });

            var chatService = new ChatService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatTools(Mock.Of<IStudentService>(), Mock.Of<ICourseService>(), Mock.Of<IEnrollmentService>()),
                mockMcpTools.Object,
                features,
                mcpOptions);

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "Show my Jira issues")]);

            mockMcpTools.Verify(m => m.GetToolsAsync(It.IsAny<CancellationToken>()), Times.Once);
            mockChatClient.Verify(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null && o.Tools != null && o.Tools.Count == 1 && o.Tools[0].Name == "jira_search"),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
