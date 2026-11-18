namespace StudentManagement.Application.Configuration;

// Effective chat settings: DB-backed overrides (AppSettings table) merged over the
// configured Chat:Features / Chat:Mcp defaults. Read per request, never startup-bound.
public class ChatSettings
{
    public bool ChatEnabled { get; set; } = true;

    public bool IncludeRag { get; set; } = false;

    public bool IncludeTools { get; set; } = false;

    public bool IncludeFullDocumentation { get; set; } = false;

    public int MaxRagChunks { get; set; } = 3;

    public bool McpEnabled { get; set; } = false;

    // Per-user daily token budget for non-admin callers; admins are unlimited.
    public int DailyTokenLimit { get; set; } = 20000;
}
