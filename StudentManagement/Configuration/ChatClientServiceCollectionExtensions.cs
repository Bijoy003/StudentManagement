using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using StudentManagement.Application.Configuration;
using System.ClientModel;

namespace StudentManagement.Configuration;

public static class ChatClientServiceCollectionExtensions
{
    public static IServiceCollection AddAppChatClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ChatOptions>(configuration.GetSection(ChatOptions.SectionName));
        services.Configure<ChatFeatureOptions>(configuration.GetSection(ChatFeatureOptions.SectionName));

        services.AddChatClient(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<ChatOptions>>().Value;
            var endpoint = options.Endpoint;
            if (!endpoint.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = $"{endpoint.TrimEnd('/')}/v1";
            }

            return new ChatClient(
                options.Model,
                new ApiKeyCredential(options.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
                .AsIChatClient();
        })
        .UseFunctionInvocation()
        .UseLogging();

        return services;
    }
}
