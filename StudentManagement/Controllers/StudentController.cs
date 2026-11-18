using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using StudentManagement.Application.Authorization;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;
using System.ComponentModel.DataAnnotations;

namespace StudentManagement.Web.Controllers
{
    [Authorize]
    public class StudentController : Controller
    {
        private readonly IStudentService _studentService;
        private readonly IEnrollmentService _enrollmentService;
        private readonly ICourseService _courseService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<StudentController> _logger;

        public StudentController(
            IStudentService studentService,
            IEnrollmentService enrollmentService,
            ICourseService courseService,
            UserManager<ApplicationUser> userManager,
            ILogger<StudentController> logger)
        {
            _studentService = studentService;
            _enrollmentService = enrollmentService;
            _courseService = courseService;
            _userManager = userManager;
            _logger = logger;
        }

        [Authorize(Policy = Policies.Staff)]
        public IActionResult Index()
        {
            return View();
        }

        [Authorize(Policy = Policies.Staff)]
        public async Task<JsonResult> GetStudentList()
        {
            var students = await _studentService.GetStudentsAsync();
            return new JsonResult(students);
        }

        [Authorize(Policy = Policies.AdminOnly)]
        public async Task<IActionResult> Create(int? id)
        {
            ViewBag.Student = null;
            if (id != null)
            {
                ViewBag.Student = await _studentService.GetStudentByIdAsync(id.Value);
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.AdminOnly)]
        public async Task<IActionResult> SaveStudent([FromBody] Student student)
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
                await _studentService.SaveStudentAsync(student);
                return Ok(true);
            }
            catch (ValidationException ex)
            {
                // Chain-of-Responsibility validation failure (StudentService) is a client
                // error: same 400 { Message } shape as the ModelState branch above.
                _logger.LogWarning(ex, "Validation failed while saving student: {StudentName}", student?.Name);
                return BadRequest(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while saving student: {StudentName}", student?.Name);
                return StatusCode(500, new { Message = "An unexpected error occurred." });
            }
        }

        [Authorize(Policy = Policies.AdminOnly)]
        public async Task<IActionResult> DeleteStudent(int id)
        {
            try
            {
                await _studentService.DeleteStudentAsync(id);
                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while deleting student: {Id}", id);
                return StatusCode(500, new { Message = "An unexpected error occurred." });
            }
        }

        [Authorize(Policy = Policies.AdminOnly)]
        public async Task<IActionResult> EnrolledInMoreThan(int courseCount = 2)
        {
            try
            {
                var students = await _studentService.GetStudentsEnrolledInMoreThan(courseCount);
                ViewBag.CourseCount = courseCount;
                return View(students);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        // Student self-service: enroll into a catalog course. The StudentId always comes
        // from the account link (AspNetUsers.StudentId), never from the request.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.StudentSelf)]
        public async Task<IActionResult> Enroll(int courseId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user?.StudentId is null)
                {
                    return RedirectToAction(nameof(MyCourses));
                }

                var course = await _courseService.GetCourseById(courseId);
                if (course is null)
                {
                    return NotFound();
                }

                if (await _enrollmentService.IsStudentEnrolledInCourseAsync(user.StudentId.Value, courseId))
                {
                    TempData["Error"] = "You are already enrolled in this course.";
                    return RedirectToAction("Index", "Course");
                }

                await _enrollmentService.SaveEnrollment(new Enrollment
                {
                    StudentId = user.StudentId.Value,
                    CourseId = courseId
                });

                TempData["Success"] = $"Enrolled in {course.Name}.";
                return RedirectToAction(nameof(MyCourses));
            }
            catch (ValidationException ex)
            {
                // Capacity (or other domain validation) failure: back to the catalog.
                TempData["Error"] = ex.Message;
                return RedirectToAction("Index", "Course");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while enrolling in course {CourseId}.", courseId);
                return View("Error");
            }
        }

        // Student self-service: drop one of OWN enrollments; ownership is checked server-side.
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Policy = Policies.StudentSelf)]
        public async Task<IActionResult> Drop(int enrollmentId)
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user?.StudentId is null)
                {
                    return RedirectToAction(nameof(MyCourses));
                }

                var enrollment = await _enrollmentService.GetEnrollmentById(enrollmentId);
                if (enrollment is null || enrollment.StudentId != user.StudentId.Value)
                {
                    return NotFound();
                }

                await _enrollmentService.DeleteEnrollment(enrollmentId);
                TempData["Success"] = "Course dropped.";
                return RedirectToAction(nameof(MyCourses));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while dropping enrollment {EnrollmentId}.", enrollmentId);
                return View("Error");
            }
        }

        // Student-role landing page: an account sees only its own enrollments. The link is
        // kept in AspNetUsers.StudentId so the query never trusts a route value.
        [Authorize(Policy = Policies.StudentSelf)]
        public async Task<IActionResult> MyCourses()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user?.StudentId is null)
                {
                    return View("MyCoursesNotLinked");
                }

                var enrollments = await _enrollmentService.GetEnrollments();
                var mine = enrollments
                    .Where(enrollment => enrollment.StudentId == user.StudentId.Value)
                    .OrderBy(enrollment => enrollment.Course?.Name)
                    .ToList();

                ViewBag.StudentName = user.FullName;
                return View(mine);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while loading the student's courses.");
                return View("Error");
            }
        }
    }
}
