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

            var chatService = new ChatService(mockChatClient.Object, mockKnowledge.Object, chatTools, features);
            var history = new List<ChatMessage> { new(ChatRole.User, "Hi") };

            var reply = await chatService.GetReplyAsync(history);

            Assert.Equal("Hello from the assistant.", reply);
            mockChatClient.Verify(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.Is<ChatOptions?>(o => o != null && o.Tools != null && o.Tools.Count > 0),
                It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
