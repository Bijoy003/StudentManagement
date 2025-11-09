using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace StudentMangement.Controllers
{
    public class HomeController : Controller
    {
        [Authorize(Roles = "Admin")]
        public IActionResult Index()
        {
            return View();
        }

        [Route("Home/Error")]
        public IActionResult Error(int? statusCode = null)
        {
            if (statusCode.HasValue)
            {
                ViewData["ErrorMessage"] = $"Error {statusCode.Value}: Page not found or forbidden.";
            }
            else
            {
                ViewData["ErrorMessage"] = "An unexpected error occurred.";
            }

            return View();
        }
    }
}
