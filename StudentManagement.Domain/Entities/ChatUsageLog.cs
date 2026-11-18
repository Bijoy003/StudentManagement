using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Domain.Entities;

// One row per completed chat request: token spend for daily quotas plus timing.
// UsageDateUtc is the UTC calendar day used to bucket the per-user daily budget.
public class ChatUsageLog
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(450)]
    public string UserId { get; set; } = string.Empty;

    public DateTime UsageDateUtc { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int TotalTokens { get; set; }

    [StringLength(128)]
    public string Model { get; set; } = string.Empty;

    public long ElapsedMs { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
