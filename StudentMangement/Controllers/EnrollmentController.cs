using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;

namespace StudentMangement.Controllers
{
    public class EnrollmentController : Controller
    {
        private readonly IEnrollmentService _enrollmentService;
        private readonly IStudentService _studentService;
        private readonly ICourseService _courseService;

        public EnrollmentController(IEnrollmentService service, IStudentService studentService, ICourseService courseService)
        {
            _enrollmentService = service;
            _studentService = studentService;
            _courseService = courseService;
        }

        public async Task<IActionResult> Index()
        {
            ViewBag.Students = await _studentService.GetStudentsAsync();
            ViewBag.Courses = await _courseService.GetAllCourses();
            var enrollments = await _enrollmentService.GetEnrollments();
            return View(enrollments);
        }

        public async Task<IActionResult> Create()
        {
            ViewBag.Students = _studentService.GetStudentsAsync();
            ViewBag.Courses = await _courseService.GetAllCourses();
            var enrollment = new Enrollment();
            return View(enrollment);
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
            catch
            {
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
            catch
            {
                return false;
            }
        }

        public async Task<IActionResult> StudentsInCourse(int? courseId)
        {
            var courses = await _courseService.GetAllCourses();
            ViewBag.Courses = new SelectList(courses, "Id", "Name", courseId);

            var students = courseId.HasValue
                ? await _enrollmentService.GetStudentsInCourse(courseId.Value)
                : new List<Student>();

            return View(students);
        }

        public async Task<IActionResult> CoursesForStudent(int? studentId)
        {
            var students = await _studentService.GetStudentsAsync();
            ViewBag.Students = new SelectList(students, "Id", "Name", studentId);

            var courses = studentId.HasValue
                ? await _enrollmentService.GetCoursesForStudent(studentId.Value)
                : new List<Course>();

            return View(courses);
        }
    }
}
