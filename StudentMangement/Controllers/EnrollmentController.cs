using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;
using StudentMangement.Services;

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

        public IActionResult Index()
        {
            ViewBag.Students = _studentService.GetStudents();
            ViewBag.Courses = _courseService.GetCourses();
            var enrollments = _enrollmentService.GetEnrollments();
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
                _enrollmentService.SaveEnrollment(enrollment);
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

        public IActionResult StudentsInCourse(int? courseId)
        {
            var courses = _courseService.GetCourses();
            ViewBag.Courses = new SelectList(courses, "Id", "Name", courseId);

            var students = courseId.HasValue
                ? _enrollmentService.GetStudentsInCourse(courseId.Value)
                : new List<Student>();

            return View(students);
        }

        public IActionResult CoursesForStudent(int? studentId)
        {
            var students = _studentService.GetStudents();
            ViewBag.Students = new SelectList(students, "Id", "Name", studentId);

            var courses = studentId.HasValue
                ? _enrollmentService.GetCoursesForStudent(studentId.Value)
                : new List<Course>();

            return View(courses);
        }
    }
}
