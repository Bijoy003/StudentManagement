using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Domain.Entities;

// Generic key/value store for admin-editable application settings. Secrets (API keys,
// connection strings) never live here — they stay in user-secrets/environment config.
public class AppSetting
{
    [Key]
    [StringLength(128)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [StringLength(2000)]
    public string Value { get; set; } = string.Empty;

    public DateTime UpdatedAtUtc { get; set; }

    [StringLength(450)]
    public string? UpdatedByUserId { get; set; }
}
