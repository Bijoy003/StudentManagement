using Microsoft.Extensions.AI;
using StudentManagement.Application.Interfaces;

namespace StudentManagement.Application.Services
{
    public class ChatService : IChatService
    {
        private const string SystemPrompt = "You are a helpful assistant for the Student Management application.";

        private readonly IChatClient _chatClient;

        public ChatService(IChatClient chatClient)
        {
            _chatClient = chatClient;
        }

        public async Task<string> GetReplyAsync(IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
        {
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, SystemPrompt)
            };
            messages.AddRange(history);

            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
            return response.Text ?? string.Empty;
        }
    }
}
