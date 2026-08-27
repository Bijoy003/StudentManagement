using Microsoft.Extensions.Options;
using StudentManagement.Application.Evaluation.Interfaces;
using StudentManagement.Application.Evaluation.Models;

namespace StudentManagement.Application.Evaluation.Services;

public sealed class PromptManager : IPromptManager
{
    private readonly PromptManagerOptions _options;
    private readonly Dictionary<string, PromptTemplate> _prompts = new(StringComparer.OrdinalIgnoreCase);
    private string _currentVersion = "v1";

    public PromptManager(IOptions<PromptManagerOptions> options)
    {
        _options = options.Value;
        RegisterDefaultPrompts();
    }

    public Task<PromptTemplate> GetPromptAsync(string version, CancellationToken cancellationToken = default)
    {
        if (_prompts.TryGetValue(version, out var prompt))
            return Task.FromResult(prompt);

        throw new KeyNotFoundException($"Prompt version '{version}' not found");
    }

    public Task<PromptTemplate> RegisterPromptAsync(PromptTemplate prompt, CancellationToken cancellationToken = default)
    {
        _prompts[prompt.Version] = prompt;
        return Task.FromResult(prompt);
    }

    public Task<IReadOnlyList<PromptTemplate>> GetAllPromptsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PromptTemplate>>(_prompts.Values.OrderBy(p => p.Version).ToList());
    }

    public Task<PromptTemplate> GetCurrentPromptAsync(CancellationToken cancellationToken = default)
    {
        return GetPromptAsync(_currentVersion, cancellationToken);
    }

    public Task SetCurrentPromptAsync(string version, CancellationToken cancellationToken = default)
    {
        if (!_prompts.ContainsKey(version))
            throw new KeyNotFoundException($"Prompt version '{version}' not found");

        _currentVersion = version;
        return Task.CompletedTask;
    }

    private void RegisterDefaultPrompts()
    {
        var v1 = PromptTemplate.Create(
            "v1",
            "Default System Prompt",
            """
            You are a helpful assistant for the Student Management application.

            ## Instructions
            - Answer questions about how to use the app using the documentation provided.
            - When the user asks for live data (students, courses, enrollments, counts, or reports), call the available tools. Do not invent records or numbers.
            - If a tool returns no data or an error, tell the user clearly.
            - Keep answers concise and formatted with markdown when helpful.
            - You cannot create, update, or delete data through chat; direct users to the appropriate page in the UI for changes.
            {{mcp_instructions}}
            {{full_documentation}}
            {{rag_context}}
            """,
            "Original system prompt with RAG, tools, and optional MCP integration",
            new Dictionary<string, string>
            {
                ["mcp_instructions"] = "",
                ["full_documentation"] = "",
                ["rag_context"] = ""
            });

        var v2 = PromptTemplate.Create(
            "v2",
            "Strict Grounding Prompt",
            """
            You are a helpful assistant for the Student Management application.

            ## Instructions
            - Answer questions about how to use the app using ONLY the documentation provided.
            - When the user asks for live data (students, courses, enrollments, counts, or reports), call the available tools. Do not invent records or numbers.
            - If a tool returns no data or an error, tell the user clearly.
            - Keep answers concise and formatted with markdown when helpful.
            - You cannot create, update, or delete data through chat; direct users to the appropriate page in the UI for changes.
            - IMPORTANT: If the answer cannot be found in the provided context or documentation, say "I don't have enough information to answer this." Do not make assumptions or use external knowledge.
            {{mcp_instructions}}
            {{full_documentation}}
            {{rag_context}}
            """,
            "Stricter prompt that requires answers to be grounded in provided context only",
            new Dictionary<string, string>
            {
                ["mcp_instructions"] = "",
                ["full_documentation"] = "",
                ["rag_context"] = ""
            });

        var v3 = PromptTemplate.Create(
            "v3",
            "Concise Prompt",
            """
            You are a concise assistant for the Student Management application.

            ## Instructions
            - Answer questions using the documentation and tools provided.
            - For live data queries, use the appropriate tools.
            - If information is not available, say "I don't have enough information."
            - Be brief. Use bullet points for lists.
            - No markdown unless explicitly helpful.
            {{mcp_instructions}}
            {{full_documentation}}
            {{rag_context}}
            """,
            "Concise variant optimized for brevity",
            new Dictionary<string, string>
            {
                ["mcp_instructions"] = "",
                ["full_documentation"] = "",
                ["rag_context"] = ""
            });

        _prompts[v1.Version] = v1;
        _prompts[v2.Version] = v2;
        _prompts[v3.Version] = v3;
    }
}

public sealed class PromptManagerOptions
{
    public string DefaultPromptVersion { get; set; } = "v1";
}