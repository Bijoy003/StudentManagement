using ChromaDB.Client;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using System.ClientModel;

namespace StudentManagement.Configuration;

public static class ChatClientServiceCollectionExtensions
{
    public static IServiceCollection AddAppChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ChatOptions>(configuration.GetSection(ChatOptions.SectionName));
        services.Configure<ChatFeatureOptions>(configuration.GetSection(ChatFeatureOptions.SectionName));
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));
        services.Configure<ChromaOptions>(configuration.GetSection(ChromaOptions.SectionName));
        services.AddSingleton<McpToolProvider>();
        services.AddSingleton<IMcpToolProvider>(sp => sp.GetRequiredService<McpToolProvider>());

        // Fail at startup on an unknown/misconfigured provider instead of on the first chat request.
        configuration.GetSection(ChatOptions.SectionName).Get<ChatOptions>()?.Validate();

        services.AddChatClient(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ChatOptions>>().Value;
            var provider = options.ResolveChatProvider();

            return new ChatClient(
                provider.Model,
                // Local/Ollama-style endpoints need no key, but ApiKeyCredential rejects
                // empty strings; fall back to the same placeholder appsettings.json uses.
                CreateCredential(provider.ApiKey),
                CreateClientOptions(provider.Endpoint))
                .AsIChatClient();
        })
        // Innermost: every provider round trip (including tool-loop iterations) reports usage.
        .Use((inner, _) => new UsageTrackingChatClient(inner))
        .UseFunctionInvocation()
        .UseLogging();

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(serviceProvider =>
        {
            var chatOptions = serviceProvider.GetRequiredService<IOptions<ChatOptions>>().Value;
            var features = serviceProvider.GetRequiredService<IOptions<ChatFeatureOptions>>().Value;
            // Embeddings can stay on a local provider (e.g. Ollama) while chat points at an
            // API-key provider; Chat:EmbeddingProvider defaults to Chat:ActiveProvider.
            var provider = chatOptions.ResolveEmbeddingProvider();

            return new EmbeddingClient(
                features.EmbeddingModel,
                // Same placeholder fallback as the ChatClient above: an empty key in the
                // embedding profile must not throw at DI-resolution time (it 500s every /Chat request).
                CreateCredential(provider.ApiKey),
                CreateClientOptions(provider.Endpoint))
                .AsIEmbeddingGenerator();
        });

        services.AddSingleton<ChromaConfigurationOptions>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ChromaOptions>>().Value;
            return new ChromaConfigurationOptions(uri: $"{options.Endpoint.TrimEnd('/')}/api/v1/");
        });

        services.AddSingleton<HttpClient>();

        services.AddSingleton<ChromaClient>(serviceProvider =>
        {
            var configOptions = serviceProvider.GetRequiredService<ChromaConfigurationOptions>();
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();
            return new ChromaClient(configOptions, httpClient);
        });

        services.AddSingleton<SemanticChunker>();
        services.AddSingleton<IVectorStore, ChromaVectorStore>();

        services.AddSingleton<IDocumentParser, TxtDocumentParser>();
        services.AddSingleton<IDocumentParser, DocxDocumentParser>();
        services.AddSingleton<IDocumentParser, PdfDocumentParser>();
        services.AddSingleton<IDocumentParser, ExcelDocumentParser>();

        return services;
    }

    internal static OpenAIClientOptions CreateClientOptions(string endpoint)
    {
        var normalized = endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? endpoint
            : $"{endpoint.TrimEnd('/')}/v1";

        return new OpenAIClientOptions
        {
            Endpoint = new Uri(normalized),
            NetworkTimeout = TimeSpan.FromMinutes(5)
        };
    }

    internal static string NormalizeApiKey(string? apiKey) =>
        string.IsNullOrWhiteSpace(apiKey) ? "unused" : apiKey!;

    private static ApiKeyCredential CreateCredential(string apiKey) =>
        new(NormalizeApiKey(apiKey));
}
