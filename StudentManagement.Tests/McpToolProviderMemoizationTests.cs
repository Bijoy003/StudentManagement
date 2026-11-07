using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Configuration;
using StudentManagement.Configuration;

namespace StudentManagement.Tests;

public class McpToolProviderMemoizationTests
{
    [Fact]
    public async Task GetToolsAsync_ConcurrentCalls_LoadsToolsOnce()
    {
        var loads = 0;
        await using var provider = new McpToolProvider(
            Options.Create(new McpOptions { Enabled = true }),
            NullLogger<McpToolProvider>.Instance,
            _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult(new List<AITool>());
            });

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => provider.GetToolsAsync()));

        Assert.Equal(1, loads);
        Assert.All(results, result => Assert.Same(results[0], result));
    }

    [Fact]
    public async Task GetToolsAsync_Disabled_ReturnsEmptyWithoutLoading()
    {
        var loads = 0;
        await using var provider = new McpToolProvider(
            Options.Create(new McpOptions { Enabled = false }),
            NullLogger<McpToolProvider>.Instance,
            _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult(new List<AITool>());
            });

        var tools = await provider.GetToolsAsync();

        Assert.Empty(tools);
        Assert.Equal(0, loads);
    }
}
