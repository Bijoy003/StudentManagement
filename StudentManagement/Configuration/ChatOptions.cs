namespace StudentManagement.Configuration;

public class ChatOptions
{
    public const string SectionName = "Chat";

    public string? ActiveProvider { get; set; }

    public string? EmbeddingProvider { get; set; }

    public Dictionary<string, ChatProviderOptions> Providers { get; set; } = new();

    public string Endpoint { get; set; } = "http://127.0.0.1:11434";

    public string Model { get; set; } = "llama3.2";

    public string ApiKey { get; set; } = "unused";

    public ChatProviderOptions ResolveChatProvider() => Resolve(ActiveProvider);

    public ChatProviderOptions ResolveEmbeddingProvider() => Resolve(EmbeddingProvider ?? ActiveProvider);

    public void Validate()
    {
        // An empty Chat:ActiveProvider keeps the pre-profile behaviour: the flat
        // Chat:Endpoint/Model/ApiKey stay authoritative even when profiles are defined.
        if (Providers.Count == 0 || string.IsNullOrWhiteSpace(ActiveProvider))
        {
            EnsureConfigured(Endpoint, "Chat:Endpoint");
            EnsureConfigured(Model, "Chat:Model");
            return;
        }

        var chatProvider = ResolveChatProvider();
        EnsureConfigured(chatProvider.Endpoint, $"Chat:Providers:{ActiveProvider}:Endpoint");
        EnsureConfigured(chatProvider.Model, $"Chat:Providers:{ActiveProvider}:Model");

        var embeddingProviderName = string.IsNullOrWhiteSpace(EmbeddingProvider) ? ActiveProvider : EmbeddingProvider;
        var embeddingProvider = Resolve(embeddingProviderName);
        EnsureConfigured(embeddingProvider.Endpoint, $"Chat:Providers:{embeddingProviderName}:Endpoint");
        EnsureConfigured(embeddingProvider.Model, $"Chat:Providers:{embeddingProviderName}:Model");
    }

    private ChatProviderOptions Resolve(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName) || Providers.Count == 0)
        {
            return new ChatProviderOptions
            {
                Endpoint = Endpoint,
                Model = Model,
                ApiKey = ApiKey
            };
        }

        var match = Providers.FirstOrDefault(pair =>
            string.Equals(pair.Key, providerName, StringComparison.OrdinalIgnoreCase));

        if (match.Value is null)
        {
            throw new InvalidOperationException(
                $"Chat provider '{providerName}' is not configured. Known providers: {DescribeKnownProviders()}.");
        }

        return match.Value;
    }

    private string DescribeKnownProviders() =>
        Providers.Count == 0 ? "none" : string.Join(", ", Providers.Keys);

    private static void EnsureConfigured(string value, string configKey)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{configKey} must be configured with a non-empty value.");
        }
    }
}
