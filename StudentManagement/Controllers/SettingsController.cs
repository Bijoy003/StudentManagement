using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Web.Models;

namespace StudentManagement.Web.Controllers;

[Authorize(Policy = Policies.AdminOnly)]
public class SettingsController : Controller
{
    private readonly IAppSettingsService _appSettings;
    private readonly IErrorLogService _errorLog;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(IAppSettingsService appSettings, IErrorLogService errorLog, ILogger<SettingsController> logger)
    {
        _appSettings = appSettings;
        _errorLog = errorLog;
        _logger = logger;
    }

    // GET: Settings
    public async Task<IActionResult> Settings()
    {
        var settings = await _appSettings.GetChatSettingsAsync();
        return View(new ChatSettingsViewModel
        {
            ChatEnabled = settings.ChatEnabled,
            IncludeRag = settings.IncludeRag,
            IncludeTools = settings.IncludeTools,
            IncludeFullDocumentation = settings.IncludeFullDocumentation,
            MaxRagChunks = settings.MaxRagChunks,
            McpEnabled = settings.McpEnabled,
            DailyTokenLimit = settings.DailyTokenLimit
        });
    }

    // POST: Settings/SaveSettings
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSettings(ChatSettingsViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Settings", model);
        }

        var settings = new ChatSettings
        {
            ChatEnabled = model.ChatEnabled,
            IncludeRag = model.IncludeRag,
            IncludeTools = model.IncludeTools,
            IncludeFullDocumentation = model.IncludeFullDocumentation,
            MaxRagChunks = model.MaxRagChunks,
            McpEnabled = model.McpEnabled,
            DailyTokenLimit = model.DailyTokenLimit
        };

        await _appSettings.SaveChatSettingsAsync(settings, User.FindFirstValue(ClaimTypes.NameIdentifier));

        _logger.LogInformation("Chat settings updated by {UserId}.", User.FindFirstValue(ClaimTypes.NameIdentifier));
        TempData["Success"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    // GET: Settings/Errors
    public async Task<IActionResult> Errors(int days = 7, string? source = null)
    {
        days = Math.Clamp(days, 1, 90);
        var sinceUtc = DateTime.UtcNow.AddDays(-days);
        var errors = await _errorLog.GetRecentAsync(200, source, sinceUtc, HttpContext.RequestAborted);

        var model = new ErrorLogListViewModel
        {
            Days = days,
            Source = source,
            Entries = errors.Select(error => new ErrorLogEntryViewModel
            {
                Id = error.Id,
                TimestampUtc = error.TimestampUtc,
                Source = error.Source,
                UserId = error.UserId,
                ExceptionType = error.ExceptionType,
                Message = error.Message,
                StackTrace = error.StackTrace,
                TraceId = error.TraceId
            }).ToList()
        };

        return View(model);
    }
}
