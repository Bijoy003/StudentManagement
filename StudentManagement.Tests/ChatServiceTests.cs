using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            var chatService = CreateService(mockChatClient.Object, mockKnowledge.Object);
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
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            var mcpTool = AIFunctionFactory.Create(() => "ok", "jira_search", "Search Jira");

            var mockMcpTools = new Mock<IMcpToolProvider>();
            mockMcpTools
                .Setup(m => m.GetToolsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([mcpTool]);

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeTools = false },
                new McpOptions { Enabled = true },
                mockMcpTools.Object);

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "Show my Jira issues")]);

            mockMcpTools.Verify(m => m.GetToolsAsync(It.IsAny<CancellationToken>()), Times.Once);
            mockChatClient.Verify(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null && o.Tools != null && o.Tools.Count == 1 && o.Tools[0].Name == "jira_search"),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task GetReplyAsync_WhenKnowledgeSearchThrows_ReturnsAssistantText()
        {
            var mockChatClient = CreateChatClient("Reply without RAG.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge
                .Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>()))
                .ThrowsAsync(new InvalidOperationException("model \"nomic-embed-text\" not found"));
            mockKnowledge.Setup(k => k.GetDocumentation()).Returns("Application documentation.");

            var chatService = CreateService(mockChatClient.Object, mockKnowledge.Object);

            var reply = await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "How many students?")]);

            Assert.Equal("Reply without RAG.", reply);
        }

        [Fact]
        public async Task GetReplyAsync_WhenDocumentationThrows_ReturnsAssistantText()
        {
            var mockChatClient = CreateChatClient("Reply without documentation.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);
            mockKnowledge
                .Setup(k => k.GetDocumentation())
                .Throws(new InvalidOperationException("ChromaDB is not reachable"));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeFullDocumentation = true, IncludeRag = false });

            var reply = await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "What can you do?")]);

            Assert.Equal("Reply without documentation.", reply);
        }

        [Fact]
        public async Task GetReplyAsync_WhenToolsDisabled_DoesNotInstructToolUse()
        {
            var mockChatClient = CreateChatClient("No tools here.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);
            mockKnowledge.Setup(k => k.GetDocumentation()).Returns("Application documentation.");

            string? systemPrompt = null;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) =>
                    systemPrompt = messages.First(m => m.Role == ChatRole.System).Text)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "No tools here.")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeTools = false, IncludeRag = false });

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "How many students?")]);

            Assert.NotNull(systemPrompt);
            Assert.DoesNotContain("call the available tools", systemPrompt, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetReplyAsync_WhenToolsEnabled_InstructsToolUse()
        {
            var mockChatClient = CreateChatClient("Tools here.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);
            mockKnowledge.Setup(k => k.GetDocumentation()).Returns("Application documentation.");

            string? systemPrompt = null;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) =>
                    systemPrompt = messages.First(m => m.Role == ChatRole.System).Text)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Tools here.")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeTools = true, IncludeRag = false });

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "How many students?")]);

            Assert.NotNull(systemPrompt);
            Assert.Contains("call the available tools", systemPrompt, StringComparison.OrdinalIgnoreCase);
        }

        private static Mock<IChatClient> CreateChatClient(string reply)
        {
            var mockChatClient = new Mock<IChatClient>();
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));
            return mockChatClient;
        }

        private static ChatService CreateService(
            IChatClient chatClient,
            IAppKnowledgeService knowledge,
            ChatFeatureOptions? features = null,
            McpOptions? mcpOptions = null,
            IMcpToolProvider? mcpToolProvider = null)
        {
            var chatTools = new ChatTools(
                Mock.Of<IStudentService>(),
                Mock.Of<ICourseService>(),
                Mock.Of<IEnrollmentService>());

            return new ChatService(
                chatClient,
                knowledge,
                chatTools,
                mcpToolProvider ?? Mock.Of<IMcpToolProvider>(),
                Options.Create(features ?? new ChatFeatureOptions()),
                Options.Create(mcpOptions ?? new McpOptions()),
                Mock.Of<ILogger<ChatService>>());
        }
    }
}
