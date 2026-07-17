using Microsoft.Extensions.AI;

namespace StudentManagement.Application.Interfaces
{
    public interface IMcpToolProvider
    {
        Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default);
    }
}
