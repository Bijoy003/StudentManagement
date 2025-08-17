using Microsoft.AspNetCore.Mvc;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;

namespace StudentMangement.Controllers
{
    public class EnrollmentController : Controller
    {
        private readonly IEnrollmentService _service;
        private readonly IStudentService _studentService;
        private readonly ICourseService _courseService;

        public EnrollmentController(IEnrollmentService service, IStudentService studentService, ICourseService courseService)
        {
            _service = service;
            _studentService = studentService;
            _courseService = courseService;
        }

        public IActionResult Index()
        {
            ViewBag.Students = _studentService.GetStudents();
            ViewBag.Courses = _courseService.GetCourses();
            var enrollments = _service.GetEnrollments();
            return View(enrollments);
        }

        public IActionResult Create()
        {
            ViewBag.Students = _studentService.GetStudents();
            ViewBag.Courses = _courseService.GetCourses();
            var enrollment = new Enrollment();
            return View(enrollment);
        }

        [HttpPost]
        public IActionResult Save([FromBody] Enrollment enrollment)
        {
            try
            {
                _service.SaveEnrollment(enrollment);
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
                _service.DeleteEnrollment(id);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
