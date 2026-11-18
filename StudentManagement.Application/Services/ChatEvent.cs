namespace StudentManagement.Application.Services;

// Events emitted by ChatService.StreamReplyAsync, mapped to SSE frames by the controller.
public abstract record ChatEvent;

public sealed record ChatStatusEvent(string Status) : ChatEvent;

public sealed record ChatDeltaEvent(string Text) : ChatEvent;

public sealed record ChatDoneEvent(long ElapsedMs, int InputTokens, int OutputTokens) : ChatEvent;

public sealed record ChatErrorEvent(string Message) : ChatEvent;