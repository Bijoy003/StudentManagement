using StudentManagement.Application.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Web.Models;

namespace StudentManagement.Web.Controllers
{
    [Authorize(Policy = Policies.AdminOnly)]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IStudentService _studentService;
        private readonly ILogger<AdminController> _logger;


        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IStudentService studentService,
            ILogger<AdminController> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _studentService = studentService;
            _logger = logger;
        }

        // GET: Admin/CreateUser
        public IActionResult CreateUser()
        {
            try
            {
                ViewBag.Roles = _roleManager.Roles.Select(r => r.Name).ToList();
                return View();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        // POST: Admin/CreateUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(CreateUserViewModel model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    ViewBag.Roles = _roleManager.Roles.Select(r => r.Name).ToList();
                    return View(model);
                }

                var user = new ApplicationUser
                {
                    UserName = model.Email,
                    Email = model.Email,
                    FullName = model.FullName
                };

                var result = await _userManager.CreateAsync(user, model.Password);

                if (result.Succeeded)
                {
                    // Assign Role
                    if (!string.IsNullOrEmpty(model.Role) && await _roleManager.RoleExistsAsync(model.Role))
                    {
                        await _userManager.AddToRoleAsync(user, model.Role);
                    }

                    TempData["Success"] = "User created successfully!";
                    return RedirectToAction(nameof(CreateUser));
                }

                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error.Description);
                }

                ViewBag.Roles = _roleManager.Roles.Select(r => r.Name).ToList();
                return View(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error ocurred.");
                return View("Error");
            }
        }

        // GET: Admin/Users
        public async Task<IActionResult> Users()
        {
            try
            {
                return View(await BuildUserManagementViewModelAsync());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while loading users.");
                return View("Error");
            }
        }

        // POST: Admin/SetRole
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetRole(SetUserRoleViewModel model)
        {
            if (!ModelState.IsValid || !AppRoles.All.Contains(model.Role))
            {
                TempData["Error"] = "Choose a valid role.";
                return RedirectToAction(nameof(Users));
            }

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user is null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction(nameof(Users));
            }

            if (user.Id == _userManager.GetUserId(User) && model.Role != AppRoles.Admin)
            {
                TempData["Error"] = "You cannot remove your own admin role.";
                return RedirectToAction(nameof(Users));
            }

            var currentRoles = await _userManager.GetRolesAsync(user);
            var appRoles = currentRoles.Where(role => AppRoles.All.Contains(role)).ToList();
            if (appRoles.Count > 0)
            {
                var removeResult = await _userManager.RemoveFromRolesAsync(user, appRoles);
                if (!removeResult.Succeeded)
                {
                    TempData["Error"] = DescribeErrors(removeResult);
                    return RedirectToAction(nameof(Users));
                }
            }

            var addResult = await _userManager.AddToRoleAsync(user, model.Role);
            if (!addResult.Succeeded)
            {
                TempData["Error"] = DescribeErrors(addResult);
                return RedirectToAction(nameof(Users));
            }

            TempData["Success"] = $"Role for {user.Email} changed to {model.Role}.";
            return RedirectToAction(nameof(Users));
        }

        // POST: Admin/ResetUserPassword
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetUserPassword(ResetUserPasswordViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Enter a new password.";
                return RedirectToAction(nameof(Users));
            }

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user is null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction(nameof(Users));
            }

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
            if (!result.Succeeded)
            {
                TempData["Error"] = DescribeErrors(result);
                return RedirectToAction(nameof(Users));
            }

            TempData["Success"] = $"Password reset for {user.Email}.";
            return RedirectToAction(nameof(Users));
        }

        // POST: Admin/LinkStudent
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LinkStudent(LinkUserStudentViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Choose a user and a student.";
                return RedirectToAction(nameof(Users));
            }

            var user = await _userManager.FindByIdAsync(model.UserId);
            if (user is null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction(nameof(Users));
            }

            if (model.StudentId is null)
            {
                user.StudentId = null;
                var unlinkResult = await _userManager.UpdateAsync(user);
                TempData[unlinkResult.Succeeded ? "Success" : "Error"] = unlinkResult.Succeeded
                    ? $"Unlinked {user.Email} from its student record."
                    : DescribeErrors(unlinkResult);
                return RedirectToAction(nameof(Users));
            }

            var student = await _studentService.GetStudentByIdAsync(model.StudentId.Value);
            if (student is null)
            {
                TempData["Error"] = "Student not found.";
                return RedirectToAction(nameof(Users));
            }

            user.StudentId = student.Id;
            try
            {
                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    TempData["Error"] = DescribeErrors(result);
                    return RedirectToAction(nameof(Users));
                }
            }
            catch (DbUpdateException ex)
            {
                // Unique filtered index: one account per student record.
                _logger.LogWarning(ex, "Student {StudentId} is already linked to another account.", student.Id);
                TempData["Error"] = "That student is already linked to another account.";
                return RedirectToAction(nameof(Users));
            }

            TempData["Success"] = $"Linked {user.Email} to {student.Name}.";
            return RedirectToAction(nameof(Users));
        }

        private async Task<UserManagementViewModel> BuildUserManagementViewModelAsync()
        {
            var students = (await _studentService.GetStudentsAsync()).ToList();
            var studentNames = students.ToDictionary(student => student.Id, student => student.Name);

            var viewModel = new UserManagementViewModel
            {
                Roles = AppRoles.All.ToList(),
                Students = students
                    .Select(student => new StudentOptionViewModel { Id = student.Id, Name = student.Name })
                    .ToList()
            };

            foreach (var user in _userManager.Users.OrderBy(user => user.Email).ToList())
            {
                var roles = await _userManager.GetRolesAsync(user);
                viewModel.Users.Add(new UserRowViewModel
                {
                    Id = user.Id,
                    Email = user.Email ?? string.Empty,
                    FullName = user.FullName,
                    Roles = roles.ToList(),
                    StudentId = user.StudentId,
                    StudentName = user.StudentId is int studentId && studentNames.TryGetValue(studentId, out var name)
                        ? name
                        : null
                });
            }

            return viewModel;
        }

        private static string DescribeErrors(IdentityResult result) =>
            string.Join(" ", result.Errors.Select(error => error.Description));
    }
}
