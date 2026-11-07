using ChromaDB.Client;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using StudentManagement.Application.Configuration;
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

        services.AddChatClient(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ChatOptions>>().Value;
            var endpoint = options.Endpoint;
            if (!endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = $"{endpoint.TrimEnd('/')}/v1";
            }

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint),
                NetworkTimeout = TimeSpan.FromMinutes(5)
            };

            return new ChatClient(
                options.Model,
                new ApiKeyCredential(options.ApiKey),
                clientOptions)
                .AsIChatClient();
        })
        .UseFunctionInvocation()
        .UseLogging();

        services.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(serviceProvider =>
        {
            var chatOptions = serviceProvider.GetRequiredService<IOptions<ChatOptions>>().Value;
            var features = serviceProvider.GetRequiredService<IOptions<ChatFeatureOptions>>().Value;
            var endpoint = chatOptions.Endpoint;
            if (!endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = $"{endpoint.TrimEnd('/')}/v1";
            }

            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint),
                NetworkTimeout = TimeSpan.FromMinutes(5)
            };

            return new EmbeddingClient(
                features.EmbeddingModel,
                new ApiKeyCredential(chatOptions.ApiKey),
                clientOptions)
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

        services.AddSingleton<IDocumentParser, TxtDocumentParser>();
        services.AddSingleton<IDocumentParser, DocxDocumentParser>();
        services.AddSingleton<IDocumentParser, PdfDocumentParser>();
        services.AddSingleton<IDocumentParser, ExcelDocumentParser>();

        return services;
    }
}