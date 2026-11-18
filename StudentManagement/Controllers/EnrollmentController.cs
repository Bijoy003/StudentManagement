using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using StudentManagement.Application.Authorization;

namespace StudentManagement.Web.Controllers
{
    [Authorize(Policy = Policies.Staff)]
    public class EnrollmentController : Controller
    {
        private readonly IEnrollmentService _enrollmentService;
        private readonly IStudentService _studentService;
        private readonly ICourseService _courseService;
        private readonly ILogger<EnrollmentController> _logger;

        public EnrollmentController(IEnrollmentService service, IStudentService studentService, ICourseService courseService, ILogger<EnrollmentController> logger)
        {
            _enrollmentService = service;
            _studentService = studentService;
            _courseService = courseService;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                ViewBag.Students = await _studentService.GetStudentsAsync();
                ViewBag.Courses = await _courseService.GetAllCourses();
                var enrollments = await _enrollmentService.GetEnrollments();
                return View(enrollments);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        public async Task<IActionResult> Create()
        {
            try
            {
                ViewBag.Students = await _studentService.GetStudentsAsync();
                ViewBag.Courses = await _courseService.GetAllCourses();
                var enrollment = new Enrollment();
                return View(enrollment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] Enrollment enrollment)
        {
            try
            {
                // The Student/Course navigation properties are never part of the JSON payload; their
                // implicit "required" validation would reject every save.
                ModelState.Remove(nameof(Enrollment.Student));
                ModelState.Remove(nameof(Enrollment.Course));

                if (!ModelState.IsValid)
                {
                    var firstError = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .FirstOrDefault();

                    return BadRequest(new { Message = firstError });
                }

                var course = await _courseService.GetCourseById(enrollment.CourseId);
                if (course is null || !CanManageCourse(course))
                {
                    return Forbid();
                }

                if (enrollment.Id != 0)
                {
                    // Updates must also own the enrollment's CURRENT course: a crafted
                    // CourseId must not move a row out of another teacher's course.
                    var persisted = await _enrollmentService.GetEnrollmentById(enrollment.Id);
                    var persistedCourse = persisted is null ? null : await _courseService.GetCourseById(persisted.CourseId);
                    if (persistedCourse is null || !CanManageCourse(persistedCourse))
                    {
                        return Forbid();
                    }
                }

                await _enrollmentService.SaveEnrollment(enrollment);
                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return Ok(false);
            }
        }

        public async Task<bool> Delete(int id)
        {
            try
            {
                var enrollment = await _enrollmentService.GetEnrollmentById(id);
                var course = enrollment is null ? null : await _courseService.GetCourseById(enrollment.CourseId);
                if (course is null || !CanManageCourse(course))
                {
                    return false;
                }

                await _enrollmentService.DeleteEnrollment(id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return false;
            }
        }

        public async Task<IActionResult> StudentsInCourse(int? courseId)
        {
            try
            {
                var courses = await _courseService.GetAllCourses();
                ViewBag.Courses = new SelectList(courses, "Id", "Name", courseId);

                var students = courseId.HasValue
                    ? await _enrollmentService.GetStudentsInCourse(courseId.Value)
                    : new List<Student>();

                return View(students);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        public async Task<IActionResult> CoursesForStudent(int? studentId)
        {
            try
            {
                var students = await _studentService.GetStudentsAsync();
                ViewBag.Students = new SelectList(students, "Id", "Name", studentId);

                var courses = studentId.HasValue
                    ? await _enrollmentService.GetCoursesForStudent(studentId.Value)
                    : new List<Course>();

                return View(courses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        // Ownership stays resource-based: admins manage every enrollment, teachers only
        // enrollments in courses they own.
        private bool CanManageCourse(Course course) =>
            User.IsInRole(AppRoles.Admin)
            || string.Equals(course.TeacherId, User.Identity?.Name, StringComparison.OrdinalIgnoreCase);
    }
}
