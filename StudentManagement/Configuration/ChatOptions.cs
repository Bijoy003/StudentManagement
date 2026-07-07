namespace StudentManagement.Configuration;

public class ChatOptions
{
    public const string SectionName = "Chat";

    public string Endpoint { get; set; } = "http://127.0.0.1:11434";

    public string Model { get; set; } = "llama3.2";

    public string ApiKey { get; set; } = "unused";
}
