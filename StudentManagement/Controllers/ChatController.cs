using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Web.Models;
using StudentManagement.Web.Services;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using ChatOptions = StudentManagement.Configuration.ChatOptions;

namespace StudentManagement.Web.Controllers
{
    [Authorize]
    public class ChatController : Controller
    {
        private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

        // Chat attachments travel as base64 inside the JSON body, so cap the payload well
        // below Kestrel's 30 MB default instead of relying on it. Exceeding the cap is a
        // 413 from the server before any parsing happens.
        private const long MaxRequestBytes = 10 * 1024 * 1024;

        private readonly IChatService _chatService;
        private readonly IEnumerable<IDocumentParser> _parsers;
        private readonly IAppSettingsService _appSettings;
        private readonly IErrorLogService _errorLog;
        private readonly IChatUsageService _chatUsage;
        private readonly ChatOptions _chatOptions;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<ChatController> _logger;
        private readonly IChatIndexModelFactory _modelFactory;

        public ChatController(
            IChatService chatService,
            IEnumerable<IDocumentParser> parsers,
            IAppSettingsService appSettings,
            IErrorLogService errorLog,
            IChatUsageService chatUsage,
            IOptions<ChatOptions> chatOptions,
            UserManager<ApplicationUser> userManager,
            ILogger<ChatController> logger,
            IChatIndexModelFactory modelFactory)
        {
            _chatService = chatService;
            _parsers = parsers;
            _appSettings = appSettings;
            _errorLog = errorLog;
            _chatUsage = chatUsage;
            _chatOptions = chatOptions.Value;
            _userManager = userManager;
            _logger = logger;
            _modelFactory = modelFactory;
        }

        public async Task<IActionResult> Index()
        {
            return View(await _modelFactory.BuildAsync(User));
        }

        // The linked student id always comes from the server-side Identity record, never the client.
        private async Task<int?> GetLinkedStudentIdAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            return user?.StudentId;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxRequestBytes)]
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

            var settings = await _appSettings.GetChatSettingsAsync(cancellationToken);
            if (!settings.ChatEnabled)
            {
                return StatusCode(503, new { error = "Chat is currently disabled by the administrator." });
            }

            var roles = GetCallerRoles();
            var isAdmin = roles.Contains(AppRoles.Admin);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Daily token quota applies to non-admins only; admins are tracked but unlimited.
            var quota = userId is null
                ? null
                : await _chatUsage.GetQuotaStatusAsync(userId, isAdmin, settings.DailyTokenLimit, cancellationToken);
            if (quota is { Exceeded: true })
            {
                return StatusCode(429, new
                {
                    error = "Daily chat token limit reached. Your quota resets at midnight UTC.",
                    used = quota.UsedToday,
                    limit = quota.DailyLimit,
                    resetsAtUtc = quota.ResetsAtUtc
                });
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
                var result = await _chatService.GetReplyWithUsageAsync(history, roles, cancellationToken, await GetLinkedStudentIdAsync());

                if (userId is not null)
                {
                    // Usage is recorded even when the reply is empty: the provider still spent tokens.
                    // Providers that do not echo the model fall back to the configured one.
                    var model = string.IsNullOrEmpty(result.Model) ? _chatOptions.ResolveChatProvider().Model : result.Model;
                    await _chatUsage.RecordAsync(userId, result.InputTokens, result.OutputTokens, model, result.ElapsedMs, CancellationToken.None);
                }

                var usedAfter = (quota?.UsedToday ?? 0) + result.InputTokens + result.OutputTokens;
                return Json(new
                {
                    reply = result.Text,
                    elapsedMs = result.ElapsedMs,
                    inputTokens = result.InputTokens,
                    outputTokens = result.OutputTokens,
                    quota = userId is null ? null : new
                    {
                        used = usedAfter,
                        limit = settings.DailyTokenLimit,
                        unlimited = isAdmin,
                        remaining = isAdmin ? (int?)null : Math.Max(0, settings.DailyTokenLimit - usedAfter)
                    }
                });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Client disconnect: not a provider failure, let the middleware skip it.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get chat reply from the configured AI provider.");
                await _errorLog.LogAsync(
                    "Chat/Send",
                    ex,
                    User.FindFirstValue(ClaimTypes.NameIdentifier),
                    Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier,
                    CancellationToken.None);
                return StatusCode(503, new { error = "The chat service is currently unavailable. Please ensure your local AI server (Ollama, LM Studio, etc.) is running and the Chat settings in appsettings are correct." });
            }
        }

        // Roles are resolved server-side from the cookie principal; the client never sends them.
        private string[] GetCallerRoles() =>
            AppRoles.All.Where(User.IsInRole).ToArray();

        // SSE variant of Send: streams status/delta/done events so the UI can show live
        // progress and tokens instead of a static spinner. Pre-stream failures still use
        // plain JSON status codes because the response has not started yet.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxRequestBytes)]
        public async Task<IActionResult> SendStream([FromBody] ChatRequest? request, CancellationToken cancellationToken)
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

            var settings = await _appSettings.GetChatSettingsAsync(cancellationToken);
            if (!settings.ChatEnabled)
            {
                return StatusCode(503, new { error = "Chat is currently disabled by the administrator." });
            }

            var roles = GetCallerRoles();
            var isAdmin = roles.Contains(AppRoles.Admin);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var quota = userId is null
                ? null
                : await _chatUsage.GetQuotaStatusAsync(userId, isAdmin, settings.DailyTokenLimit, cancellationToken);
            if (quota is { Exceeded: true })
            {
                return StatusCode(429, new
                {
                    error = "Daily chat token limit reached. Your quota resets at midnight UTC.",
                    used = quota.UsedToday,
                    limit = quota.DailyLimit,
                    resetsAtUtc = quota.ResetsAtUtc
                });
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
                _logger.LogWarning(ex, "Failed to parse a chat attachment.");
                return BadRequest(new { error = "The attached file could not be read." });
            }

            Response.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            var usedBefore = quota?.UsedToday ?? 0;
            ChatDoneEvent? done = null;
            try
            {
                var linkedStudentId = await GetLinkedStudentIdAsync();
                await foreach (var chatEvent in _chatService.StreamReplyAsync(history, roles, cancellationToken, linkedStudentId))
                {
                    if (chatEvent is ChatDoneEvent doneEvent)
                    {
                        done = doneEvent;
                    }

                    var payload = MapEvent(chatEvent, userId, isAdmin, settings.DailyTokenLimit, usedBefore);
                    await WriteEventAsync(payload, cancellationToken);
                }

                if (done is not null && userId is not null)
                {
                    var model = _chatOptions.ResolveChatProvider().Model;
                    await _chatUsage.RecordAsync(userId, done.InputTokens, done.OutputTokens, model, done.ElapsedMs, CancellationToken.None);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Client disconnected mid-stream: not an application error.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chat streaming failed.");
                await _errorLog.LogAsync(
                    "Chat/SendStream",
                    ex,
                    userId,
                    Activity.Current?.TraceId.ToString() ?? HttpContext.TraceIdentifier,
                    CancellationToken.None);
                try
                {
                    await WriteEventAsync(new { type = "error", error = "The chat service is currently unavailable. Please ensure your local AI server (Ollama, LM Studio, etc.) is running and the Chat settings in appsettings are correct." }, CancellationToken.None);
                }
                catch
                {
                    // The response may already be closed; the failure is logged either way.
                }
            }

            return new EmptyResult();
        }

        private static object MapEvent(ChatEvent chatEvent, string? userId, bool isAdmin, int dailyLimit, int usedBefore) => chatEvent switch
        {
            ChatStatusEvent status => new { type = "status", status = status.Status },
            ChatDeltaEvent delta => new { type = "delta", text = delta.Text },
            ChatDoneEvent done => new
            {
                type = "done",
                elapsedMs = done.ElapsedMs,
                inputTokens = done.InputTokens,
                outputTokens = done.OutputTokens,
                quota = userId is null ? null : new
                {
                    used = usedBefore + done.InputTokens + done.OutputTokens,
                    limit = dailyLimit,
                    unlimited = isAdmin,
                    remaining = isAdmin ? (int?)null : Math.Max(0, dailyLimit - usedBefore - done.InputTokens - done.OutputTokens)
                }
            },
            ChatErrorEvent error => new { type = "error", error = error.Message },
            _ => new { type = "unknown" }
        };

        private async Task WriteEventAsync(object payload, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(payload);
            await Response.WriteAsync($"data: {json}\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
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
