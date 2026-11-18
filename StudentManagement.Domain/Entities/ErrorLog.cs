using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Domain.Entities;

// App-wide error capture (exception middleware + explicit chat failure logging).
// Writes are best-effort: a logging failure must never mask the original exception.
public class ErrorLog
{
    [Key]
    public int Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    [Required]
    [StringLength(512)]
    public string Source { get; set; } = string.Empty;

    [StringLength(450)]
    public string? UserId { get; set; }

    [StringLength(512)]
    public string? ExceptionType { get; set; }

    [Required]
    [StringLength(4000)]
    public string Message { get; set; } = string.Empty;

    public string? StackTrace { get; set; }

    [StringLength(128)]
    public string? TraceId { get; set; }
}
