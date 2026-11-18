using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace StudentManagement.Application.Services;

// Sits innermost in the chat client pipeline (inside UseFunctionInvocation) so every
// provider round trip of a tool loop contributes its usage to the request's accumulator.
// The accumulator flows via AsyncLocal, keeping the singleton client safe for concurrent requests.
public sealed class UsageTrackingChatClient : DelegatingChatClient
{
    private static readonly AsyncLocal<UsageAccumulator?> CurrentAccumulator = new();

    public UsageTrackingChatClient(IChatClient innerClient) : base(innerClient)
    {
    }

    public static IDisposable BeginScope(out UsageAccumulator accumulator)
    {
        accumulator = new UsageAccumulator();
        var previous = CurrentAccumulator.Value;
        CurrentAccumulator.Value = accumulator;
        return new ResetScope(previous);
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        Accumulate(response.Usage);
        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            // Streaming usage arrives as UsageContent items, not a property on the update.
            foreach (var usageContent in update.Contents.OfType<UsageContent>())
            {
                Accumulate(usageContent.Details);
            }

            yield return update;
        }
    }

    private static void Accumulate(UsageDetails? usage)
    {
        if (CurrentAccumulator.Value is { } accumulator && usage is not null)
        {
            accumulator.InputTokens += usage.InputTokenCount ?? 0;
            accumulator.OutputTokens += usage.OutputTokenCount ?? 0;
        }
    }

    private sealed class ResetScope(UsageAccumulator? previous) : IDisposable
    {
        public void Dispose() => CurrentAccumulator.Value = previous;
    }
}

public sealed class UsageAccumulator
{
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}