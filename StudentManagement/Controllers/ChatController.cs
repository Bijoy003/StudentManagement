using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using StudentManagement.Application.Interfaces;
using StudentManagement.Web.Models;

namespace StudentManagement.Web.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private readonly IChatService _chatService;
        private readonly ILogger<ChatController> _logger;

        public ChatController(IChatService chatService, ILogger<ChatController> logger)
        {
            _chatService = chatService;
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
            if (string.IsNullOrWhiteSpace(lastMessage.Text))
            {
                return BadRequest(new { error = "Message text cannot be empty." });
            }

            var history = request.Messages
                .Select(m => new ChatMessage(ParseRole(m.Role), m.Text))
                .ToList();

            try
            {
                var reply = await _chatService.GetReplyAsync(history, cancellationToken);
                return Json(new { reply });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get chat reply from LM Studio.");
                return StatusCode(503, new { error = "The chat service is currently unavailable. Please ensure LM Studio is running with the local server enabled." });
            }
        }

        private static ChatRole ParseRole(string role) => role.ToLowerInvariant() switch
        {
            "assistant" => ChatRole.Assistant,
            "system" => ChatRole.System,
            _ => ChatRole.User
        };
    }
}
