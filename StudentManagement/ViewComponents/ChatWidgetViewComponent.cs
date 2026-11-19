using Microsoft.AspNetCore.Mvc;
using StudentManagement.Application.Interfaces;
using StudentManagement.Web.Services;

namespace StudentManagement.Web.ViewComponents;

// Floating chat widget rendered by the layout on every authenticated page except
// the full Chat page. Anonymous callers and disabled chat render nothing.
public class ChatWidgetViewComponent : ViewComponent
{
    private readonly IChatIndexModelFactory _modelFactory;
    private readonly IAppSettingsService _appSettings;

    public ChatWidgetViewComponent(IChatIndexModelFactory modelFactory, IAppSettingsService appSettings)
    {
        _modelFactory = modelFactory;
        _appSettings = appSettings;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var user = ViewContext.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            return Content(string.Empty);
        }

        var settings = await _appSettings.GetChatSettingsAsync();
        if (!settings.ChatEnabled)
        {
            return Content(string.Empty);
        }

        return View(await _modelFactory.BuildAsync(user));
    }
}
