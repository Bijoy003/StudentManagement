using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Interfaces;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Web.Models;

namespace StudentManagement.Web.Services;

// Builds the chat view model for both the full Chat page and the floating widget,
// so quota, role and capability rules live in exactly one place.
public class ChatIndexModelFactory : IChatIndexModelFactory
{
    private readonly IAppSettingsService _appSettings;
    private readonly IChatUsageService _chatUsage;
    private readonly UserManager<ApplicationUser> _userManager;

    public ChatIndexModelFactory(
        IAppSettingsService appSettings,
        IChatUsageService chatUsage,
        UserManager<ApplicationUser> userManager)
    {
        _appSettings = appSettings;
        _chatUsage = chatUsage;
        _userManager = userManager;
    }

    public async Task<ChatIndexViewModel> BuildAsync(ClaimsPrincipal user)
    {
        var settings = await _appSettings.GetChatSettingsAsync();
        var roles = AppRoles.All.Where(user.IsInRole).ToArray();
        var isAdmin = roles.Contains(AppRoles.Admin);
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var linkedStudentId = await GetLinkedStudentIdAsync(user);

        var quota = userId is null
            ? null
            : await _chatUsage.GetQuotaStatusAsync(userId, isAdmin, settings.DailyTokenLimit);

        return new ChatIndexViewModel
        {
            ChatEnabled = settings.ChatEnabled,
            IsAdmin = isAdmin,
            Unlimited = quota?.IsUnlimited ?? isAdmin,
            UsedToday = quota?.UsedToday ?? 0,
            DailyLimit = settings.DailyTokenLimit,
            Role = roles.FirstOrDefault() ?? AppRoles.User,
            HasStudentLink = linkedStudentId is not null,
            ToolsEnabled = settings.IncludeTools,
            CapabilityHint = BuildCapabilityHint(roles, linkedStudentId, settings.IncludeTools)
        };
    }

    private static string BuildCapabilityHint(string[] roles, int? linkedStudentId, bool toolsEnabled)
    {
        if (!toolsEnabled)
        {
            return "Live data tools are disabled by the administrator; the assistant answers from documentation only.";
        }

        if (AppRoles.IsStaff(roles))
        {
            return "You can query students, courses, enrollments, counts, and grades.";
        }

        if (linkedStudentId is not null)
        {
            return "You can ask about your own courses, grades, and profile, plus the course catalog.";
        }

        return "You can browse the course catalog. Ask an admin to link your account to a student record (Admin → Users) to query your own courses and grades.";
    }

    // The linked student id always comes from the server-side Identity record, never the client.
    private async Task<int?> GetLinkedStudentIdAsync(ClaimsPrincipal user)
    {
        var appUser = await _userManager.GetUserAsync(user);
        return appUser?.StudentId;
    }
}
