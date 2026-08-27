using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Web.Models;

namespace StudentManagement.Web.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly IChatService _chatService;
        private readonly IEnumerable<IDocumentParser> _parsers;
        private readonly ILogger<ChatController> _logger;

        public ChatController(IChatService chatService, IEnumerable<IDocumentParser> parsers, ILogger<ChatController> logger)
        {
            _chatService = chatService;
            _parsers = parsers;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Send([FromBody] ChatRequest request, CancellationToken cancellationToken)
        {
            if (request.Messages is null || request.Messages.Count == 0)
            {
                return BadRequest(new { error = "At least one message is required." });
            }

            var lastMessage = request.Messages[^1];
            if (string.IsNullOrWhiteSpace(lastMessage.Text) && string.IsNullOrWhiteSpace(lastMessage.FileData))
            {
                return BadRequest(new { error = "Message text or file is required." });
            }

            var history = new List<ChatMessage>();
            var imageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
            foreach (var m in request.Messages)
            {
                var role = ParseRole(m.Role);
                if (!string.IsNullOrWhiteSpace(m.FileData) && !string.IsNullOrWhiteSpace(m.FileName))
                {
                    var ext = Path.GetExtension(m.FileName)?.ToLowerInvariant();
                    if (ext != null && imageExtensions.Contains(ext))
                    {
                        var contents = new List<AIContent>();
                        if (!string.IsNullOrWhiteSpace(m.Text))
                            contents.Add(new TextContent(m.Text));
                        var mimeType = ext switch
                        {
                            ".png" => "image/png",
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".gif" => "image/gif",
                            ".webp" => "image/webp",
                            _ => "image/png"
                        };
                        contents.Add(new DataContent(m.FileData, mimeType));
                        history.Add(new ChatMessage(role, contents));
                    }
                    else
                    {
                        var fileText = await ParseFileContent(m.FileName, m.FileData);
                        var combinedText = string.IsNullOrWhiteSpace(m.Text)
                            ? $"[Attached file: {m.FileName}]\n\n{fileText}"
                            : $"{m.Text}\n\n[Attached file: {m.FileName}]\n\n{fileText}";
                        history.Add(new ChatMessage(role, combinedText));
                    }
                }
                else
                {
                    history.Add(new ChatMessage(role, m.Text));
                }
            }

            try
            {
                var reply = await _chatService.GetReplyAsync(history, cancellationToken);
                return Json(new { reply });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get chat reply from the configured AI provider.");
                return StatusCode(503, new { error = "The chat service is currently unavailable. Please ensure your local AI server (Ollama, LM Studio, etc.) is running and the Chat settings in appsettings are correct." });
            }
        }

        private static ChatRole ParseRole(string role) => role.ToLowerInvariant() switch
        {
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };

        private async Task<string> ParseFileContent(string fileName, string fileData)
        {
            var parser = _parsers.FirstOrDefault(p => p.CanParse(fileName));
            if (parser == null)
                return $"[Unsupported file type: {fileName}]";

            var bytes = Convert.FromBase64String(fileData);
            using var stream = new MemoryStream(bytes);
            return await parser.ExtractTextAsync(stream);
        }
    }
}
