using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentManagement.Application.Authorization;

namespace StudentManagement.Web.Controllers
{
    // Catalog reads are open to any signed-in role (matching the chat catalog tools);
    // create/edit/delete stay Staff-only, with ownership checks on the teacher's own rows.
    [Authorize]
    public class CourseController : Controller
    {
        private readonly ICourseService _courseService;
        private readonly ILogger<CourseController> _logger;

        public CourseController(ICourseService service, ILogger<CourseController> logger)
        {
            _courseService = service;
            _logger = logger;
        }

        // Client-side caching is requested here, but these pages render the layout's antiforgery
        // token (logout form) and DefaultAntiforgery forces 'no-cache, no-store' on the response.
        // See docs/caching.md - type 3.
        [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Client)]
        public async Task<IActionResult> Index()
        {
            try
            {
                var courses = await _courseService.GetAllCourses();
                return View(courses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        [Authorize(Policy = Policies.Staff)]
        public async Task<IActionResult> Create(int? id)
        {
            try
            {
                Course? course;

                if (id == null)
                {
                    course = new Course();
                }
                else
                {
                    course = await _courseService.GetCourseById(id.Value);
                    if (course is null)
                    {
                        return NotFound();
                    }

                    if (!CanManageCourse(course))
                    {
                        return Forbid();
                    }
                }

                return View(course);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        [HttpPost]
        [Authorize(Policy = Policies.Staff)]
        public async Task<IActionResult> Save([FromBody] Course course)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var firstError = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .FirstOrDefault();

                    return BadRequest(new { Message = firstError });
                }

                if (course.Id != 0)
                {
                    // Ownership is checked against the persisted row, never the bound
                    // payload — a crafted TeacherId must not grant edit rights.
                    var persisted = await _courseService.GetCourseById(course.Id);
                    if (persisted is null || !CanManageCourse(persisted))
                    {
                        return Forbid();
                    }
                }

                // Teachers own what they create; admins may assign ownership explicitly.
                if (course.Id == 0 && !User.IsInRole(AppRoles.Admin))
                {
                    course.TeacherId = User.Identity?.Name;
                }

                await _courseService.SaveCourse(course);
                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return Ok(false);
            }
        }

        [Authorize(Policy = Policies.Staff)]
        public async Task<bool> Delete(int id)
        {
            try
            {
                var course = await _courseService.GetCourseById(id);
                if (course is null || !CanManageCourse(course))
                {
                    return false;
                }

                await _courseService.DeleteCourse(id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return false;
            }
        }

        [ResponseCache(Duration = 300, Location = ResponseCacheLocation.Client)]
        public async Task<IActionResult> StudentCountPerCourse()
        {
            try
            {
                var data = await _courseService.GetStudentCountPerCourse();
                return View(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        // Ownership stays resource-based: admins manage every course, teachers only their own.
        private bool CanManageCourse(Course course) =>
            User.IsInRole(AppRoles.Admin)
            || string.Equals(course.TeacherId, User.Identity?.Name, StringComparison.OrdinalIgnoreCase);
    }
}
