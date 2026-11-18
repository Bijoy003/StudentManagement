namespace StudentManagement.Application.Services;

// A chat reply plus the provider usage/timing metadata the controller needs for
// quota accounting and the UI footer. GetReplyAsync stays text-only for the
// evaluation CLI and other callers that do not care about usage.
public sealed record ChatReplyResult(string Text, int InputTokens, int OutputTokens, long ElapsedMs, string Model);
