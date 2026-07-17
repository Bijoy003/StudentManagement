using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;

namespace StudentManagement.Configuration;

public sealed class McpToolProvider : IMcpToolProvider, IAsyncDisposable
{
    private readonly McpOptions _options;
    private readonly ILogger<McpToolProvider> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly List<McpClient> _clients = [];
    private IReadOnlyList<AITool>? _cachedTools;

    public McpToolProvider(IOptions<McpOptions> options, ILogger<McpToolProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return [];
        }

        if (_cachedTools is not null)
        {
            return _cachedTools;
        }

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedTools is not null)
            {
                return _cachedTools;
            }

            var tools = new List<AITool>();

            foreach (var (serverKey, server) in _options.Servers)
            {
                if (!server.Enabled)
                {
                    continue;
                }

                try
                {
                    var transport = CreateTransport(serverKey, server);
                    var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
                    _clients.Add(client);

                    var serverTools = await client.ListToolsAsync(cancellationToken: cancellationToken);
                    foreach (var tool in serverTools)
                    {
                        tools.Add(tool.WithName($"{serverKey}_{tool.Name}"));
                    }

                    _logger.LogInformation(
                        "Connected to MCP server '{ServerKey}' and loaded {ToolCount} tools.",
                        serverKey,
                        serverTools.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to connect to MCP server '{ServerKey}'. Skipping.", serverKey);
                }
            }

            _cachedTools = tools;
            return _cachedTools;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static IClientTransport CreateTransport(string serverKey, McpServerEntry server)
    {
        return server.Transport.ToLowerInvariant() switch
        {
            "stdio" => CreateStdioTransport(serverKey, server),
            "sse" or "http" => throw new NotSupportedException(
                $"MCP transport '{server.Transport}' is not supported yet. Use 'stdio' for local development."),
            _ => throw new NotSupportedException($"MCP transport '{server.Transport}' is not supported.")
        };
    }

    private static StdioClientTransport CreateStdioTransport(string serverKey, McpServerEntry server)
    {
        if (string.IsNullOrWhiteSpace(server.Command))
        {
            throw new InvalidOperationException($"MCP server '{serverKey}' requires a Command when using stdio transport.");
        }

        var environment = server.Environment?.ToDictionary(
            static pair => pair.Key,
            static pair => (string?)pair.Value,
            StringComparer.Ordinal);

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = serverKey,
            Command = server.Command,
            Arguments = server.Arguments,
            EnvironmentVariables = environment
        });
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _clients)
        {
            await client.DisposeAsync();
        }

        _clients.Clear();
        _initLock.Dispose();
    }
}
