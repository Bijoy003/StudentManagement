using Microsoft.Extensions.AI;
using Moq;
using StudentManagement.Application.Services;

namespace StudentManagement.Tests;

public class UsageTrackingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_TwoCallsWithUsage_ReturnsAccumulatedTotals()
    {
        var inner = new Mock<IChatClient>();
        inner
            .SetupSequence(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResponseWithUsage(10, 20))
            .ReturnsAsync(ResponseWithUsage(5, 7));
        var client = new UsageTrackingChatClient(inner.Object);

        using var scope = UsageTrackingChatClient.BeginScope(out var accumulator);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")]);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "two")]);

        Assert.Equal(15, accumulator.InputTokens);
        Assert.Equal(27, accumulator.OutputTokens);
    }

    [Fact]
    public async Task GetResponseAsync_NoUsage_ReturnsZeroAccumulation()
    {
        var inner = new Mock<IChatClient>();
        inner
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        var client = new UsageTrackingChatClient(inner.Object);

        using var scope = UsageTrackingChatClient.BeginScope(out var accumulator);
        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "one")]);

        Assert.Equal(0, accumulator.InputTokens);
        Assert.Equal(0, accumulator.OutputTokens);
    }

    [Fact]
    public async Task BeginScope_Disposed_ReturnsPreviousAccumulator()
    {
        var inner = new Mock<IChatClient>();
        inner
            .Setup(c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ResponseWithUsage(3, 4));
        var client = new UsageTrackingChatClient(inner.Object);

        using var outerScope = UsageTrackingChatClient.BeginScope(out var outer);
        using (var innerScope = UsageTrackingChatClient.BeginScope(out var nested))
        {
            await client.GetResponseAsync([new ChatMessage(ChatRole.User, "nested")]);
            Assert.Equal(3, nested.InputTokens);
        }

        await client.GetResponseAsync([new ChatMessage(ChatRole.User, "outer")]);

        Assert.Equal(3, outer.InputTokens);
        Assert.Equal(4, outer.OutputTokens);
    }

    [Fact]
    public async Task GetStreamingResponseAsync_UpdatesWithUsage_ReturnsAccumulatedTotals()
    {
        var inner = new Mock<IChatClient>();
        inner
            .Setup(c => c.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(Updates(
                UpdateWithUsage("chunk", 8, 2),
                UpdateWithUsage("more", 1, 6)));
        var client = new UsageTrackingChatClient(inner.Object);

        using var scope = UsageTrackingChatClient.BeginScope(out var accumulator);
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, "one")]))
        {
            Assert.NotNull(update);
        }

        Assert.Equal(9, accumulator.InputTokens);
        Assert.Equal(8, accumulator.OutputTokens);
    }

    private static ChatResponse ResponseWithUsage(long input, long output) =>
        new(new ChatMessage(ChatRole.Assistant, "ok"))
        {
            Usage = new UsageDetails { InputTokenCount = input, OutputTokenCount = output }
        };

    private static ChatResponseUpdate UpdateWithUsage(string text, long input, long output)
    {
        var update = new ChatResponseUpdate(ChatRole.Assistant, text);
        update.Contents.Add(new UsageContent(new UsageDetails { InputTokenCount = input, OutputTokenCount = output }));
        return update;
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Updates(params ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            yield return update;
            await Task.Yield();
        }
    }
}