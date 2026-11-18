using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StudentManagement.Application.Authorization;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Web.Controllers
{
    // Catalog reads are open to any signed-in role (matching the chat catalog tools);
    // create/edit/delete stay Staff-only, with ownership checks on the teacher's own rows.
    [Authorize]
    public class CourseController : Controller
    {
        private readonly ICourseService _courseService;
        private readonly IEnrollmentService _enrollmentService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<CourseController> _logger;

        public CourseController(
            ICourseService service,
            IEnrollmentService enrollmentService,
            UserManager<ApplicationUser> userManager,
            ILogger<CourseController> logger)
        {
            _courseService = service;
            _enrollmentService = enrollmentService;
            _userManager = userManager;
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

                // Student catalog: mark the account's own enrollments and the full courses
                // so the view can offer Enroll / show Enrolled / Full. Staff stays unchanged.
                if (User.IsInRole(AppRoles.Student))
                {
                    var enrolledCourseIds = new HashSet<int>();
                    var fullCourseIds = new HashSet<int>();
                    var user = await _userManager.GetUserAsync(User);
                    if (user?.StudentId is int studentId)
                    {
                        var enrollments = (await _enrollmentService.GetEnrollments()).ToList();
                        enrolledCourseIds = enrollments
                            .Where(enrollment => enrollment.StudentId == studentId)
                            .Select(enrollment => enrollment.CourseId)
                            .ToHashSet();
                        var counts = enrollments
                            .GroupBy(enrollment => enrollment.CourseId)
                            .ToDictionary(group => group.Key, group => group.Count());
                        fullCourseIds = courses
                            .Where(course => course.MaxEnrollments is int limit
                                && counts.TryGetValue(course.Id, out var count)
                                && count >= limit)
                            .Select(course => course.Id)
                            .ToHashSet();
                    }
                    ViewBag.EnrolledCourseIds = enrolledCourseIds;
                    ViewBag.FullCourseIds = fullCourseIds;
                }

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

                Course? persisted = null;
                if (course.Id != 0)
                {
                    // Ownership is checked against the persisted row, never the bound
                    // payload — a crafted TeacherId must not grant edit rights.
                    persisted = await _courseService.GetCourseById(course.Id);
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

                // The seat cap is admin-managed: strip it on teacher creates and restamp
                // the persisted value on teacher edits so a payload cannot raise it.
                if (!User.IsInRole(AppRoles.Admin))
                {
                    course.MaxEnrollments = persisted?.MaxEnrollments;
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
