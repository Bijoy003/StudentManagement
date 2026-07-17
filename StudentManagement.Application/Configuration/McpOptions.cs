namespace StudentManagement.Application.Configuration
{
    public class McpOptions
    {
        public const string SectionName = "Chat:Mcp";

        public bool Enabled { get; set; }

        public Dictionary<string, McpServerEntry> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class McpServerEntry
    {
        public bool Enabled { get; set; } = true;

        public string Transport { get; set; } = "stdio";

        public string? Command { get; set; }

        public string[]? Arguments { get; set; }

        public Dictionary<string, string>? Environment { get; set; }

        public string? Url { get; set; }
    }
}
