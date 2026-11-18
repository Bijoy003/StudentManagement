using Microsoft.Extensions.AI;

namespace StudentManagement.Application.Interfaces
{
    public interface IChatService
    {
        Task<string> GetReplyAsync(
            IReadOnlyList<ChatMessage> history,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default);
    }
}
