using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Configuration;

namespace StudentManagement.Tests;

public class ChatConfigurationTests
{
    [Fact]
    public void CreateClientOptions_EndpointWithoutV1_ReturnsUriWithV1Suffix()
    {
        var options = ChatClientServiceCollectionExtensions.CreateClientOptions("http://localhost:11434");

        Assert.Equal("http://localhost:11434/v1", options.Endpoint!.AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void CreateClientOptions_EndpointWithV1_ReturnsEndpointUnchanged()
    {
        var options = ChatClientServiceCollectionExtensions.CreateClientOptions("https://opencode.ai/inference/openai/v1");

        Assert.Equal("https://opencode.ai/inference/openai/v1", options.Endpoint!.AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void CreateClientOptions_EndpointWithTrailingSlash_ReturnsTrimmedV1Uri()
    {
        var options = ChatClientServiceCollectionExtensions.CreateClientOptions("http://localhost:11434/");

        Assert.Equal("http://localhost:11434/v1", options.Endpoint!.AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void CreateClientOptions_WhenCalled_ReturnsFiveMinuteNetworkTimeout()
    {
        var options = ChatClientServiceCollectionExtensions.CreateClientOptions("http://localhost:11434");

        Assert.Equal(TimeSpan.FromMinutes(5), options.NetworkTimeout);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeApiKey_EmptyKey_ReturnsUnusedPlaceholder(string? apiKey)
    {
        Assert.Equal("unused", ChatClientServiceCollectionExtensions.NormalizeApiKey(apiKey));
    }

    [Fact]
    public void NormalizeApiKey_ConfiguredKey_ReturnsSameKey()
    {
        Assert.Equal("secret", ChatClientServiceCollectionExtensions.NormalizeApiKey("secret"));
    }

    [Fact]
    public async Task AddAppChatClient_WhenConfigured_ReturnsFourDocumentParsers()
    {
        await using var provider = BuildProvider();

        var parsers = provider.GetServices<IDocumentParser>().ToList();

        Assert.Equal(4, parsers.Count);
        Assert.Contains(parsers, parser => parser is TxtDocumentParser);
        Assert.Contains(parsers, parser => parser is DocxDocumentParser);
        Assert.Contains(parsers, parser => parser is PdfDocumentParser);
        Assert.Contains(parsers, parser => parser is ExcelDocumentParser);
    }

    [Fact]
    public async Task AddAppChatClient_WhenConfigured_ReturnsVectorStoreAndChunker()
    {
        await using var provider = BuildProvider();

        Assert.IsType<ChromaVectorStore>(provider.GetRequiredService<IVectorStore>());
        Assert.NotNull(provider.GetRequiredService<SemanticChunker>());
    }

    [Fact]
    public async Task AddAppChatClient_WhenConfigured_ReturnsSameMcpProviderForInterface()
    {
        await using var provider = BuildProvider();

        var concrete = provider.GetRequiredService<McpToolProvider>();
        var abstraction = provider.GetRequiredService<IMcpToolProvider>();

        Assert.Same(concrete, abstraction);
    }

    [Fact]
    public async Task AddAppChatClient_WhenConfigured_ReturnsChatClientAndEmbeddings()
    {
        await using var provider = BuildProvider();

        Assert.NotNull(provider.GetRequiredService<Microsoft.Extensions.AI.IChatClient>());
        Assert.NotNull(provider.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>());
    }

    [Fact]
    public void AddAppChatClient_UnknownActiveProvider_ReturnsStartupException()
    {
        var configuration = BuildConfiguration(
            ("Chat:ActiveProvider", "Nope"),
            ("Chat:Providers:Ollama:Endpoint", "http://localhost:11434"),
            ("Chat:Providers:Ollama:Model", "qwen2.5:3b-instruct"));

        var services = new ServiceCollection();
        services.AddLogging();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddAppChatClient(configuration));

        Assert.Contains("Nope", exception.Message);
    }

    [Fact]
    public async Task AddAppChatClient_EmptyApiKey_ReturnsConfiguredServices()
    {
        var configuration = BuildConfiguration(
            ("Chat:Endpoint", "http://localhost:11434"),
            ("Chat:Model", "qwen2.5:3b-instruct"),
            ("Chat:ApiKey", ""));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppChatClient(configuration);

        await using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<Microsoft.Extensions.AI.IChatClient>());
        Assert.NotNull(provider.GetRequiredService<Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>>());
    }

    private static ServiceProvider BuildProvider()
    {
        var configuration = BuildConfiguration(
            ("Chat:Endpoint", "http://localhost:11434"),
            ("Chat:Model", "qwen2.5:3b-instruct"),
            ("Chroma:Endpoint", "http://localhost:8000"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAppChatClient(configuration);

        return services.BuildServiceProvider();
    }

    private static IConfiguration BuildConfiguration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(pair => pair.Key, pair => (string?)pair.Value))
            .Build();
}
