using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;

namespace StudentMangement.Controllers
{
    [Authorize]
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
                if (!ModelState.IsValid)
                {
                    var firstError = ModelState.Values
                        .SelectMany(v => v.Errors)
                        .Select(e => e.ErrorMessage)
                        .FirstOrDefault();

                    return BadRequest(new { Message = firstError });
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

        public bool Delete(int id)
        {
            try
            {
                _enrollmentService.DeleteEnrollment(id);
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
    }
}
