using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Web.Models;

public class ChatSettingsViewModel
{
    [Display(Name = "Chat enabled")]
    public bool ChatEnabled { get; set; } = true;

    [Display(Name = "Include RAG retrieval")]
    public bool IncludeRag { get; set; }

    [Display(Name = "Include data tools")]
    public bool IncludeTools { get; set; }

    [Display(Name = "Include full documentation")]
    public bool IncludeFullDocumentation { get; set; }

    [Display(Name = "Max RAG chunks")]
    [Range(1, 20, ErrorMessage = "Max RAG chunks must be between 1 and 20.")]
    public int MaxRagChunks { get; set; } = 3;

    [Display(Name = "MCP integrations enabled")]
    public bool McpEnabled { get; set; }

    [Display(Name = "Daily token limit (non-admin users)")]
    [Range(0, 10000000, ErrorMessage = "Daily token limit must be between 0 and 10,000,000.")]
    public int DailyTokenLimit { get; set; } = 20000;
}
