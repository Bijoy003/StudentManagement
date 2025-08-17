using Microsoft.AspNetCore.Mvc;

namespace StudentMangement.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
