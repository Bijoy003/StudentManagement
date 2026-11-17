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
        private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

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
        public async Task<IActionResult> Send([FromBody] ChatRequest? request, CancellationToken cancellationToken)
        {
            if (request?.Messages is null || request.Messages.Count == 0)
            {
                return BadRequest(new { error = "At least one message is required." });
            }

            if (request.Messages.Any(m => m is null))
            {
                return BadRequest(new { error = "Each message is required." });
            }

            var lastMessage = request.Messages[^1];
            if (string.IsNullOrWhiteSpace(lastMessage.Text) && string.IsNullOrWhiteSpace(lastMessage.FileData))
            {
                return BadRequest(new { error = "Message text or file is required." });
            }

            List<ChatMessage> history;
            try
            {
                history = await BuildHistoryAsync(request.Messages);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A malformed attachment must be a client error, not the provider-unavailable 503
                // and definitely not an unhandled 500.
                _logger.LogWarning(ex, "Failed to parse a chat attachment.");
                return BadRequest(new { error = "The attached file could not be read." });
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

        private async Task<List<ChatMessage>> BuildHistoryAsync(List<ChatMessageDto> messages)
        {
            var history = new List<ChatMessage>();

            foreach (var m in messages)
            {
                if (m is null)
                {
                    throw new ArgumentException("Each message is required.", nameof(messages));
                }

                var role = ParseRole(m.Role);
                var text = m.Text ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(m.FileData) && !string.IsNullOrWhiteSpace(m.FileName))
                {
                    var fileName = m.FileName;
                    var fileData = m.FileData;
                    var ext = Path.GetExtension(fileName).ToLowerInvariant();

                    if (ImageExtensions.Contains(ext))
                    {
                        var contents = new List<AIContent>();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            contents.Add(new TextContent(text));
                        }

                        var bytes = Convert.FromBase64String(fileData);
                        contents.Add(new DataContent(bytes, GetImageMimeType(ext)) { Name = fileName });
                        history.Add(new ChatMessage(role, contents));
                    }
                    else
                    {
                        var fileText = await ParseFileContent(fileName, fileData);
                        var combinedText = string.IsNullOrWhiteSpace(text)
                            ? $"[Attached file: {fileName}]\n\n{fileText}"
                            : $"{text}\n\n[Attached file: {fileName}]\n\n{fileText}";
                        history.Add(new ChatMessage(role, combinedText));
                    }
                }
                else
                {
                    history.Add(new ChatMessage(role, text));
                }
            }

            return history;
        }

        private static ChatRole ParseRole(string? role) => role?.ToLowerInvariant() switch
        {
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };

        private static string GetImageMimeType(string extension) => extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "image/png"
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
