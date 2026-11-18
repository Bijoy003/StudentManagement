namespace StudentManagement.Web.Models;

public class ErrorLogListViewModel
{
    public int Days { get; set; } = 7;
    public string? Source { get; set; }
    public List<ErrorLogEntryViewModel> Entries { get; set; } = [];
}

public class ErrorLogEntryViewModel
{
    public int Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? ExceptionType { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? StackTrace { get; set; }
    public string? TraceId { get; set; }
}
