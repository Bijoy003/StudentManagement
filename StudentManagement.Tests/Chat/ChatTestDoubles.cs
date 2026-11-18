using System.Text.Json;
using Microsoft.Extensions.AI;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;

namespace StudentManagement.Tests;

// Shared fakes for the chat assistant suite. They keep every test deterministic and
// independent of Ollama, ChromaDB, and the real OpenAI client.
internal sealed class RecordingChatClient : IChatClient
{
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse>> _responders = new();
    private readonly Queue<Func<IReadOnlyList<ChatMessage>, ChatOptions?, IAsyncEnumerable<ChatResponseUpdate>>> _streamResponders = new();

    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];
    public List<ChatOptions?> Options { get; } = [];
    public List<CancellationToken> Tokens { get; } = [];

    public RecordingChatClient()
    {
    }

    public RecordingChatClient(params string[] replies)
    {
        foreach (var reply in replies)
        {
            Enqueue(reply);
        }
    }

    public void Enqueue(string reply) =>
        Enqueue((_, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)));

    public void Enqueue(ChatResponse response) =>
        Enqueue((_, _) => response);

    public void Enqueue(Func<IReadOnlyList<ChatMessage>, ChatOptions?, ChatResponse> responder) =>
        _responders.Enqueue(responder);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = messages.ToList();
        Requests.Add(request);
        Options.Add(options);
        Tokens.Add(cancellationToken);

        var responder = _responders.Count > 0
            ? _responders.Dequeue()
            : (_, _) => new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok"));

        return Task.FromResult(responder(request, options));
    }

    public void EnqueueStream(params ChatResponseUpdate[] updates) =>
        EnqueueStream((_, _) => ToUpdates(updates));

    public void EnqueueStream(Func<IReadOnlyList<ChatMessage>, ChatOptions?, IAsyncEnumerable<ChatResponseUpdate>> responder) =>
        _streamResponders.Enqueue(responder);

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = messages.ToList();
        Requests.Add(request);
        Options.Add(options);
        Tokens.Add(cancellationToken);

        var responder = _streamResponders.Count > 0
            ? _streamResponders.Dequeue()
            : (_, _) => ToUpdates(new ChatResponseUpdate(ChatRole.Assistant, "ok"));

        return responder(request, options);
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToUpdates(params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            yield return update;
            await Task.Yield();
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class FakeEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Func<string, float[]> _selector;

    public FakeEmbeddingGenerator(Func<string, float[]>? selector = null)
    {
        _selector = selector ?? (_ => new float[384]);
    }

    public int Calls { get; private set; }
    public List<IReadOnlyList<string>> Batches { get; } = [];

    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Calls++;
        var batch = values.ToList();
        Batches.Add(batch);

        var embeddings = batch
            .Select(value => new Embedding<float>(_selector(value)))
            .ToList();

        return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}

internal sealed class FakeVectorStore : IVectorStore
{
    public List<VectorDocument> Documents { get; } = [];
    public List<VectorSearchResult> Results { get; } = [];
    public int ReplaceCalls { get; private set; }
    public int QueryCalls { get; private set; }
    public int LastMaxResults { get; private set; }
    public ReadOnlyMemory<float> LastEmbedding { get; private set; }

    public Task ReplaceAsync(IReadOnlyList<VectorDocument> documents, CancellationToken cancellationToken = default)
    {
        ReplaceCalls++;
        Documents.Clear();
        Documents.AddRange(documents);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorSearchResult>> QueryAsync(
        ReadOnlyMemory<float> embedding,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        QueryCalls++;
        LastEmbedding = embedding;
        LastMaxResults = maxResults;
        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(Results);
    }
}

internal sealed class StubDocumentParser : IDocumentParser
{
    private readonly Func<string, bool> _canParse;
    private readonly Func<string> _content;

    public StubDocumentParser(Func<string, bool>? canParse = null, Func<string>? content = null)
    {
        _canParse = canParse ?? (_ => true);
        _content = content ?? (() => "stub document text");
    }

    public int ParseCalls { get; private set; }

    public bool CanParse(string fileName) => _canParse(fileName);

    public Task<string> ExtractTextAsync(Stream stream)
    {
        ParseCalls++;
        return Task.FromResult(_content());
    }
}

internal static class ChatJson
{
    public static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
