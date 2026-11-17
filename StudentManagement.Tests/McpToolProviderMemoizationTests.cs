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

    [Fact]
    public async Task GetToolsAsync_AfterLoadFailure_RetriesAndReturnsTools()
    {
        var attempts = 0;
        var tool = AIFunctionFactory.Create(() => "ok", "jira_search", "Search Jira");

        await using var provider = new McpToolProvider(
            Options.Create(new McpOptions { Enabled = true }),
            NullLogger<McpToolProvider>.Instance,
            _ =>
            {
                attempts++;
                if (attempts == 1)
                {
                    throw new InvalidOperationException("first load failed");
                }

                return Task.FromResult(new List<AITool> { tool });
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetToolsAsync());
        var tools = await provider.GetToolsAsync();

        Assert.Equal(2, attempts);
        Assert.Same(tool, Assert.Single(tools));
    }

    [Fact]
    public async Task GetToolsAsync_SequentialCalls_ReturnsSameCachedToolsWithoutReload()
    {
        var loads = 0;
        var tool = AIFunctionFactory.Create(() => "ok", "jira_search", "Search Jira");

        await using var provider = new McpToolProvider(
            Options.Create(new McpOptions { Enabled = true }),
            NullLogger<McpToolProvider>.Instance,
            _ =>
            {
                Interlocked.Increment(ref loads);
                return Task.FromResult(new List<AITool> { tool });
            });

        var first = await provider.GetToolsAsync();
        var second = await provider.GetToolsAsync();

        Assert.Equal(1, loads);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetToolsAsync_CancelledToken_ThrowsOperationCanceled()
    {
        await using var provider = new McpToolProvider(
            Options.Create(new McpOptions { Enabled = true }),
            NullLogger<McpToolProvider>.Instance,
            token => Task.FromCanceled<List<AITool>>(token));

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetToolsAsync(cts.Token));
    }

    [Fact]
    public async Task GetToolsAsync_UnsupportedTransport_ReturnsEmptyAndSkipsServer()
    {
        await using var provider = new McpToolProvider(
            Options.Create(new McpOptions
            {
                Enabled = true,
                Servers =
                {
                    ["jira"] = new McpServerEntry { Transport = "sse", Url = "https://example.test/sse" }
                }
            }),
            NullLogger<McpToolProvider>.Instance);

        var tools = await provider.GetToolsAsync();

        Assert.Empty(tools);
    }

    [Fact]
    public async Task GetToolsAsync_StdioWithoutCommand_ReturnsEmptyAndSkipsServer()
    {
        await using var provider = new McpToolProvider(
            Options.Create(new McpOptions
            {
                Enabled = true,
                Servers =
                {
                    ["jira"] = new McpServerEntry { Transport = "stdio", Command = null }
                }
            }),
            NullLogger<McpToolProvider>.Instance);

        var tools = await provider.GetToolsAsync();

        Assert.Empty(tools);
    }

    [Fact]
    public async Task GetToolsAsync_DisabledServer_ReturnsEmptyWithoutConnecting()
    {
        await using var provider = new McpToolProvider(
            Options.Create(new McpOptions
            {
                Enabled = true,
                Servers =
                {
                    ["jira"] = new McpServerEntry { Enabled = false, Command = "npx" }
                }
            }),
            NullLogger<McpToolProvider>.Instance);

        var tools = await provider.GetToolsAsync();

        Assert.Empty(tools);
    }

    [Fact]
    public async Task DisposeAsync_ProviderWithoutClients_ReturnsCompletedTask()
    {
        var provider = new McpToolProvider(
            Options.Create(new McpOptions { Enabled = false }),
            NullLogger<McpToolProvider>.Instance);

        await provider.DisposeAsync();
    }
}
