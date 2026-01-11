using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using StudentManagement.Models;
using StudentMangement.Abstraction.Services;
using StudentMangement.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace StudentMangement.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<AccountController> _logger;
        private readonly IAuthService _authService;

        public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ILogger<AccountController> logger, IAuthService authService)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
            _authService = authService;
        }

        [HttpGet]
        public IActionResult Login() => View();

        //[HttpPost]
        //public async Task<IActionResult> Login(LoginViewModel model)
        //{
        //    try
        //    {
        //        if (!ModelState.IsValid) return View(model);

        //        var user = await _userManager.FindByEmailAsync(model.Email);
        //        if (user != null)
        //        {
        //            var result = await _signInManager.PasswordSignInAsync(user, model.Password, model.RememberMe, false);
        //            if (result.Succeeded) return RedirectToAction("Index", "Home");
        //        }

        //        ModelState.AddModelError("", "Invalid login attempt");
        //        return View(model);
        //    }
        //    catch (Exception ex)
        //    {
        //        _logger.LogError(ex, "Error while fetching courses");
        //        return View("Error");
        //    }
        //}

        [HttpPost]
        public async Task<IActionResult> Login([FromBody] LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user == null)
                return Unauthorized("Invalid login attempt");

            var isPasswordValid = await _userManager.CheckPasswordAsync(user, model.Password);
            if (!isPasswordValid)
                return Unauthorized("Invalid login attempt");

            var token = _authService.GenerateJwtToken(user);

            Response.Cookies.Append(
                        "access_token",
                        token,
                        new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = true,
                            SameSite = SameSiteMode.Lax,
                            Expires = DateTimeOffset.UtcNow.AddHours(1)
                        });

            return Ok();
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            var users = new[]
            {
                new { Id = 1, Name = "John Doe", Email = "john@test.com" },
                new { Id = 2, Name = "Jane Smith", Email = "jane@test.com" },
                new { Id = 3, Name = "Alex Brown", Email = "alex@test.com" }
            };

            return Ok(users);
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            try
            {
                await _signInManager.SignOutAsync();
                return RedirectToAction("Login", "Account");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while fetching courses");
                return View("Error");
            }
        }

    }
}
