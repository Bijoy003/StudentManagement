using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;

namespace StudentManagement.Application.Services
{
    public class ChatService : IChatService
    {
        private readonly IChatClient _chatClient;
        private readonly IAppKnowledgeService _appKnowledge;
        private readonly ChatTools _chatTools;
        private readonly IMcpToolProvider _mcpToolProvider;
        private readonly IAppSettingsService _appSettings;
        private readonly ILogger<ChatService> _logger;

        public ChatService(
            IChatClient chatClient,
            IAppKnowledgeService appKnowledge,
            ChatTools chatTools,
            IMcpToolProvider mcpToolProvider,
            IAppSettingsService appSettings,
            ILogger<ChatService> logger)
        {
            _chatClient = chatClient;
            _appKnowledge = appKnowledge;
            _chatTools = chatTools;
            _mcpToolProvider = mcpToolProvider;
            _appSettings = appSettings;
            _logger = logger;
        }

        public async Task<string> GetReplyAsync(
            IReadOnlyList<ChatMessage> history,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default,
            int? linkedStudentId = null)
        {
            var result = await GetReplyWithUsageAsync(history, roles, cancellationToken, linkedStudentId);
            return result.Text;
        }

        public async Task<ChatReplyResult> GetReplyWithUsageAsync(
            IReadOnlyList<ChatMessage> history,
            IReadOnlyCollection<string> roles,
            CancellationToken cancellationToken = default,
            int? linkedStudentId = null)
        {
            // DB-backed settings are read per request so admin changes apply without a restart.
            var settings = await _appSettings.GetChatSettingsAsync(cancellationToken);

            var userQuery = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

            var canUseStudentData = AppRoles.IsStaff(roles);

            var ragContext = settings.IncludeRag
                ? await TryGetRagContextAsync(userQuery, settings.MaxRagChunks)
                : string.Empty;

            var tools = new List<AITool>();
            if (settings.IncludeTools)
            {
                tools.AddRange(_chatTools.GetToolsFor(roles, linkedStudentId));
            }

            if (settings.McpEnabled && canUseStudentData)
            {
                tools.AddRange(await _mcpToolProvider.GetToolsAsync(cancellationToken));
            }

            var hasSelfTools = settings.IncludeTools && linkedStudentId is not null && !canUseStudentData;
            var systemPrompt = BuildSystemPrompt(settings, ragContext, tools.Count > 0, canUseStudentData, hasSelfTools);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt)
            };
            messages.AddRange(history);

            var options = new ChatOptions();
            if (tools.Count > 0)
            {
                options.Tools = tools;
            }

            var stopwatch = Stopwatch.StartNew();
            using var usageScope = UsageTrackingChatClient.BeginScope(out var accumulated);
            var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
            stopwatch.Stop();

            var text = response.Text ?? string.Empty;
            var (inputTokens, outputTokens) = ResolveUsage(accumulated, response.Usage, messages, text);
            var model = response.AdditionalProperties is not null
                && response.AdditionalProperties.TryGetValue("model", out var modelValue)
                ? modelValue?.ToString() ?? string.Empty
                : string.Empty;
            return new ChatReplyResult(text, inputTokens, outputTokens, stopwatch.ElapsedMilliseconds, model);
        }

        public async IAsyncEnumerable<ChatEvent> StreamReplyAsync(
            IReadOnlyList<ChatMessage> history,
            IReadOnlyCollection<string> roles,
            [EnumeratorCancellation] CancellationToken cancellationToken = default,
            int? linkedStudentId = null)
        {
            var settings = await _appSettings.GetChatSettingsAsync(cancellationToken);
            var userQuery = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
            var canUseStudentData = AppRoles.IsStaff(roles);

            var ragContext = string.Empty;
            if (settings.IncludeRag)
            {
                yield return new ChatStatusEvent("Searching documentation…");
                ragContext = await TryGetRagContextAsync(userQuery, settings.MaxRagChunks);
            }

            var tools = new List<AITool>();
            if (settings.IncludeTools)
            {
                tools.AddRange(_chatTools.GetToolsFor(roles, linkedStudentId));
            }

            if (settings.McpEnabled && canUseStudentData)
            {
                yield return new ChatStatusEvent("Loading external integrations…");
                tools.AddRange(await _mcpToolProvider.GetToolsAsync(cancellationToken));
            }

            var hasSelfTools = settings.IncludeTools && linkedStudentId is not null && !canUseStudentData;
            var systemPrompt = BuildSystemPrompt(settings, ragContext, tools.Count > 0, canUseStudentData, hasSelfTools);

            var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
            messages.AddRange(history);

            var options = new ChatOptions();
            if (tools.Count > 0)
            {
                options.Tools = tools;
            }

            yield return new ChatStatusEvent("Generating reply…");

            var stopwatch = Stopwatch.StartNew();
            using var usageScope = UsageTrackingChatClient.BeginScope(out var accumulated);
            var fullText = new StringBuilder();
            long updateInputTokens = 0;
            long updateOutputTokens = 0;

            await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, options, cancellationToken))
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent text when !string.IsNullOrEmpty(text.Text):
                            fullText.Append(text.Text);
                            yield return new ChatDeltaEvent(text.Text);
                            break;
                        case FunctionCallContent call:
                            yield return new ChatStatusEvent($"Calling tool: {call.Name}…");
                            break;
                        case UsageContent usage:
                            updateInputTokens += usage.Details.InputTokenCount ?? 0;
                            updateOutputTokens += usage.Details.OutputTokenCount ?? 0;
                            break;
                    }
                }
            }

            stopwatch.Stop();

            // With the real pipeline the accumulator already sums every tool-loop round trip;
            // without it (tests, bare clients) fall back to the usage seen in the updates.
            var effective = accumulated.InputTokens > 0 || accumulated.OutputTokens > 0
                ? accumulated
                : new UsageAccumulator { InputTokens = updateInputTokens, OutputTokens = updateOutputTokens };
            var (inputTokens, outputTokens) = ResolveUsage(effective, null, messages, fullText.ToString());

            yield return new ChatDoneEvent(stopwatch.ElapsedMilliseconds, inputTokens, outputTokens);
        }

        // Prefer the pipeline accumulator (sums every tool-loop round trip), then the final
        // response's usage, then a ~4-chars-per-token estimate for providers that report none.
        private static (int InputTokens, int OutputTokens) ResolveUsage(
            UsageAccumulator accumulated,
            UsageDetails? usage,
            List<ChatMessage> messages,
            string replyText)
        {
            if (accumulated.InputTokens > 0 || accumulated.OutputTokens > 0)
            {
                return ((int)Math.Min(accumulated.InputTokens, int.MaxValue), (int)Math.Min(accumulated.OutputTokens, int.MaxValue));
            }

            if (usage is not null && (usage.InputTokenCount > 0 || usage.OutputTokenCount > 0))
            {
                return ((int)(usage.InputTokenCount ?? 0), (int)(usage.OutputTokenCount ?? 0));
            }

            var inputChars = messages.Sum(m => m.Text?.Length ?? 0);
            return (Math.Max(1, inputChars / 4), replyText.Length / 4);
        }

        // RAG is best-effort context: a missing embedding model or an unreachable ChromaDB
        // must downgrade the answer, not fail the whole chat request with a 503.
        private async Task<string> TryGetRagContextAsync(string userQuery, int maxRagChunks)
        {
            try
            {
                return await _appKnowledge.SearchRelevantChunks(userQuery, maxRagChunks);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RAG retrieval failed; continuing without retrieved documentation.");
                return string.Empty;
            }
        }

        private string BuildSystemPrompt(ChatSettings settings, string ragContext, bool hasTools, bool canUseStudentData, bool hasSelfTools)
        {
            var instructions = new List<string>
            {
                "- Answer questions about how to use the app using the documentation provided."
            };

            // Only instruct tool use when tools are actually declared. Small local models
            // (e.g. gemma4:e2b) otherwise emit a tool call that the provider drops, leaving
            // an empty reply.
            if (hasTools && canUseStudentData)
            {
                instructions.Add("- When the user asks for live data (students, courses, enrollments, counts, or reports), call the available tools. Do not invent records or numbers.");
            }
            else if (hasTools && hasSelfTools)
            {
                instructions.Add("- You can look up the signed-in student's own courses, grades, and profile with the GetMyCourses, GetMyGrades, and GetMyProfile tools. Never reveal or guess other students' records.");
            }
            else if (hasTools)
            {
                instructions.Add("- You can only look up the course catalog (course names, credits, and enrollment counts). You do not have access to student records, rosters, enrollments, or grades; say so plainly and never claim or guess those details.");
            }

            instructions.Add("- If a tool returns no data or an error, tell the user clearly.");
            instructions.Add("- Keep answers concise and formatted with markdown when helpful.");
            instructions.Add("- You cannot create, update, or delete data through chat; direct users to the appropriate page in the UI for changes.");

            var prompt = $"""
                You are a helpful assistant for the Student Management application.

                ## Instructions
                {string.Join(Environment.NewLine, instructions)}
                """;

            if (settings.McpEnabled && canUseStudentData)
            {
                prompt += """


                    ## External integrations (MCP)
                    - When the user asks about Jira issues, projects, or Atlassian data, use the jira_* tools.
                    - When the user asks about GitHub repositories, issues, or pull requests, use the github_* tools.
                    - Only use external integration tools when the question clearly requires that external system.
                    - Do not invent issue keys, PR numbers, or repository data; always call the appropriate tool first.
                    """;
            }

            if (settings.IncludeFullDocumentation)
            {
                var documentation = TryGetDocumentation();
                if (!string.IsNullOrWhiteSpace(documentation))
                {
                    prompt += $"""


                        ## Application documentation
                        {documentation}
                        """;
                }
            }

            if (!string.IsNullOrWhiteSpace(ragContext))
            {
                prompt += $"""


                    ## Relevant documentation for this question
                    {ragContext}
                    """;
            }

            return prompt;
        }

        private string TryGetDocumentation()
        {
            try
            {
                return _appKnowledge.GetDocumentation();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Loading the embedded application documentation failed; continuing without it.");
                return string.Empty;
            }
        }
    }
}
