using Microsoft.Extensions.AI;
using StudentManagement.Application.Services;
using StudentManagement.Application.Services;

namespace StudentManagement.Application.Interfaces
{
    public interface IChatService
    {
        Task<string> GetReplyAsync(
            IReadOnlyList<ChatMessage> history,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default,
            int? linkedStudentId = null);

        Task<ChatReplyResult> GetReplyWithUsageAsync(
            IReadOnlyList<ChatMessage> history,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default,
            int? linkedStudentId = null);

        IAsyncEnumerable<ChatEvent> StreamReplyAsync(
            IReadOnlyList<ChatMessage> history,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default,
            int? linkedStudentId = null);
    }
}
