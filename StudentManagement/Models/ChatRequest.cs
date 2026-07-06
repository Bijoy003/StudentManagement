namespace StudentManagement.Web.Models
{
    public class ChatRequest
    {
        public List<ChatMessageDto> Messages { get; set; } = [];
    }

    public class ChatMessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
    }
}
