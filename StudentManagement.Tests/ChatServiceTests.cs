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

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeTools = true });
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

        [Fact]
        public async Task GetReplyAsync_WhenRagEnabled_UsesLastUserMessageAndIncludesContext()
        {
            var mockChatClient = CreateChatClient("Answer with RAG.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.GetDocumentation()).Returns("FULL DOCS");
            mockKnowledge
                .Setup(k => k.SearchRelevantChunks("How many students?", 3))
                .ReturnsAsync("RELEVANT CONTEXT");

            string? systemPrompt = null;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) =>
                    systemPrompt = messages.First(m => m.Role == ChatRole.System).Text)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Answer with RAG.")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeRag = true, IncludeFullDocumentation = false });

            var history = new List<ChatMessage>
            {
                new(ChatRole.User, "Earlier question"),
                new(ChatRole.Assistant, "Earlier answer"),
                new(ChatRole.User, "How many students?")
            };

            var reply = await chatService.GetReplyAsync(history);

            Assert.Equal("Answer with RAG.", reply);
            mockKnowledge.Verify(k => k.SearchRelevantChunks("How many students?", 3), Times.Once);
            Assert.NotNull(systemPrompt);
            Assert.Contains("## Relevant documentation for this question", systemPrompt);
            Assert.Contains("RELEVANT CONTEXT", systemPrompt);
        }

        [Fact]
        public async Task GetReplyAsync_WhenRagDisabled_DoesNotSearchKnowledge()
        {
            var mockChatClient = CreateChatClient("No RAG.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync("unused");

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeRag = false });

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "How many students?")]);

            mockKnowledge.Verify(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public async Task GetReplyAsync_WhenFullDocumentationEnabled_AppendsDocumentation()
        {
            var mockChatClient = CreateChatClient("With docs.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.GetDocumentation()).Returns("FULL DOCS");
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            string? systemPrompt = null;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) =>
                    systemPrompt = messages.First(m => m.Role == ChatRole.System).Text)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "With docs.")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeFullDocumentation = true, IncludeRag = false });

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "What can you do?")]);

            Assert.NotNull(systemPrompt);
            Assert.Contains("## Application documentation", systemPrompt);
            Assert.Contains("FULL DOCS", systemPrompt);
        }

        [Fact]
        public async Task GetReplyAsync_WhenDocumentationIsWhitespace_OmitsDocumentationSection()
        {
            var mockChatClient = CreateChatClient("No docs.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.GetDocumentation()).Returns("   ");
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            string? systemPrompt = null;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) =>
                    systemPrompt = messages.First(m => m.Role == ChatRole.System).Text)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "No docs.")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeFullDocumentation = true, IncludeRag = false });

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "What can you do?")]);

            Assert.NotNull(systemPrompt);
            Assert.DoesNotContain("## Application documentation", systemPrompt);
        }

        [Fact]
        public async Task GetReplyAsync_WhenToolsEnabled_DeclaresAllChatToolNames()
        {
            var mockChatClient = CreateChatClient("Tools declared.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            ChatOptions? captured = null;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, options, _) => captured = options)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Tools declared.")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeTools = true, IncludeRag = false });

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "How many students?")]);

            Assert.NotNull(captured);
            Assert.NotNull(captured!.Tools);
            var expected = new[]
            {
                "ListStudents", "GetStudentById", "GetStudentsEnrolledInMoreThan", "ListCourses",
                "GetCourseById", "GetStudentCountPerCourse", "ListEnrollments", "GetStudentsInCourse",
                "GetCoursesForStudent"
            };

            Assert.Equal(expected.OrderBy(n => n), captured.Tools!.Select(t => t.Name).OrderBy(n => n));
        }

        [Fact]
        public async Task GetReplyAsync_WhenMcpEnabled_InstructsExternalTools()
        {
            var mockChatClient = CreateChatClient("Jira reply.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            var mockMcpTools = new Mock<IMcpToolProvider>();
            mockMcpTools
                .Setup(m => m.GetToolsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([AIFunctionFactory.Create(() => "ok", "jira_search", "Search Jira")]);

            string? systemPrompt = null;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) =>
                    systemPrompt = messages.First(m => m.Role == ChatRole.System).Text)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Jira reply.")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeTools = false, IncludeRag = false },
                new McpOptions { Enabled = true },
                mockMcpTools.Object);

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "Show my Jira issues")]);

            Assert.NotNull(systemPrompt);
            Assert.Contains("## External integrations (MCP)", systemPrompt);
            Assert.Contains("jira_*", systemPrompt);
        }

        [Fact]
        public async Task GetReplyAsync_WhenMcpEnabledWithNoTools_DoesNotInstructToolUse()
        {
            var mockChatClient = CreateChatClient("No tools.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            var mockMcpTools = new Mock<IMcpToolProvider>();
            mockMcpTools
                .Setup(m => m.GetToolsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);

            string? systemPrompt = null;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) =>
                    systemPrompt = messages.First(m => m.Role == ChatRole.System).Text)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "No tools.")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeTools = false, IncludeRag = false },
                new McpOptions { Enabled = true },
                mockMcpTools.Object);

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "Hello")]);

            Assert.NotNull(systemPrompt);
            Assert.DoesNotContain("call the available tools", systemPrompt, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task GetReplyAsync_WhenMcpDisabled_DoesNotRequestMcpTools()
        {
            var mockChatClient = CreateChatClient("No MCP.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            var mockMcpTools = new Mock<IMcpToolProvider>();
            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeRag = false },
                new McpOptions { Enabled = false },
                mockMcpTools.Object);

            await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "Hello")]);

            mockMcpTools.Verify(m => m.GetToolsAsync(It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task GetReplyAsync_WhenProviderReturnsNoText_ReturnsEmptyString()
        {
            var mockChatClient = new Mock<IChatClient>();
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ChatResponse());

            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeRag = false });

            var reply = await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "Hello")]);

            Assert.Equal(string.Empty, reply);
        }

        [Fact]
        public async Task GetReplyAsync_WithHistory_PrependsSingleSystemMessageAndPreservesOrder()
        {
            var mockChatClient = CreateChatClient("Ordered.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            IReadOnlyList<ChatMessage>? captured = null;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((messages, _, _) => captured = messages.ToList())
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Ordered.")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeRag = false });

            await chatService.GetReplyAsync(
            [
                new ChatMessage(ChatRole.User, "first"),
                new ChatMessage(ChatRole.Assistant, "second"),
                new ChatMessage(ChatRole.User, "third")
            ]);

            Assert.NotNull(captured);
            Assert.Equal(4, captured!.Count);
            Assert.Equal(ChatRole.System, captured[0].Role);
            Assert.Equal("first", captured[1].Text);
            Assert.Equal("second", captured[2].Text);
            Assert.Equal("third", captured[3].Text);
        }

        [Fact]
        public async Task GetReplyAsync_ForwardsCancellationToken_ReturnsReply()
        {
            var mockChatClient = CreateChatClient("Cancelled?");
            var mockKnowledge = new Mock<IAppKnowledgeService>();
            mockKnowledge.Setup(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(string.Empty);

            CancellationToken observed = default;
            mockChatClient
                .Setup(c => c.GetResponseAsync(
                    It.IsAny<IEnumerable<ChatMessage>>(),
                    It.IsAny<ChatOptions?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>((_, _, token) => observed = token)
                .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Cancelled?")));

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeRag = false });

            using var cts = new CancellationTokenSource();
            var reply = await chatService.GetReplyAsync([new ChatMessage(ChatRole.User, "Hello")], cts.Token);

            Assert.Equal("Cancelled?", reply);
            Assert.Equal(cts.Token, observed);
        }

        [Fact]
        public async Task GetReplyAsync_EmptyHistory_ReturnsAssistantText()
        {
            var mockChatClient = CreateChatClient("Hello from the assistant.");
            var mockKnowledge = new Mock<IAppKnowledgeService>();

            var chatService = CreateService(
                mockChatClient.Object,
                mockKnowledge.Object,
                new ChatFeatureOptions { IncludeRag = false });

            var reply = await chatService.GetReplyAsync([]);

            Assert.Equal("Hello from the assistant.", reply);
            mockKnowledge.Verify(k => k.SearchRelevantChunks(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
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
