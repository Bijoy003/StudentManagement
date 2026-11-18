namespace StudentManagement.Web.Models;

public class ChatIndexViewModel
{
    public bool ChatEnabled { get; set; } = true;
    public bool IsAdmin { get; set; }
    public bool Unlimited { get; set; }
    public int UsedToday { get; set; }
    public int DailyLimit { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool HasStudentLink { get; set; }
    public bool ToolsEnabled { get; set; }
    public string CapabilityHint { get; set; } = string.Empty;
}
