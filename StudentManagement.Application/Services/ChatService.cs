using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
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
        private readonly ChatFeatureOptions _features;
        private readonly McpOptions _mcpOptions;

        public ChatService(
            IChatClient chatClient,
            IAppKnowledgeService appKnowledge,
            ChatTools chatTools,
            IMcpToolProvider mcpToolProvider,
            IOptions<ChatFeatureOptions> features,
            IOptions<McpOptions> mcpOptions)
        {
            _chatClient = chatClient;
            _appKnowledge = appKnowledge;
            _chatTools = chatTools;
            _mcpToolProvider = mcpToolProvider;
            _features = features.Value;
            _mcpOptions = mcpOptions.Value;
        }

        public async Task<string> GetReplyAsync(IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken = default)
        {
            var userQuery = history.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;

            var ragContext = _features.IncludeRag
                ? await _appKnowledge.SearchRelevantChunks(userQuery, _features.MaxRagChunks)
                : string.Empty;

            var systemPrompt = BuildSystemPrompt(ragContext);

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt)
            };
            messages.AddRange(history);

            var tools = new List<AITool>();
            if (_features.IncludeTools)
            {
                tools.AddRange(_chatTools.GetTools());
            }

            if (_mcpOptions.Enabled)
            {
                tools.AddRange(await _mcpToolProvider.GetToolsAsync(cancellationToken));
            }

            var options = new ChatOptions();
            if (tools.Count > 0)
            {
                options.Tools = tools;
            }

            var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
            return response.Text ?? string.Empty;
        }

        private string BuildSystemPrompt(string ragContext)
        {
            var prompt = """
                You are a helpful assistant for the Student Management application.

                ## Instructions
                - Answer questions about how to use the app using the documentation provided.
                - When the user asks for live data (students, courses, enrollments, counts, or reports), call the available tools. Do not invent records or numbers.
                - If a tool returns no data or an error, tell the user clearly.
                - Keep answers concise and formatted with markdown when helpful.
                - You cannot create, update, or delete data through chat; direct users to the appropriate page in the UI for changes.
                """;

            if (_mcpOptions.Enabled)
            {
                prompt += """


                    ## External integrations (MCP)
                    - When the user asks about Jira issues, projects, or Atlassian data, use the jira_* tools.
                    - When the user asks about GitHub repositories, issues, or pull requests, use the github_* tools.
                    - Only use external integration tools when the question clearly requires that external system.
                    - Do not invent issue keys, PR numbers, or repository data; always call the appropriate tool first.
                    """;
            }

            if (_features.IncludeFullDocumentation)
            {
                var documentation = _appKnowledge.GetDocumentation();
                prompt += $"""


                    ## Application documentation
                    {documentation}
                    """;
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
    }
}
