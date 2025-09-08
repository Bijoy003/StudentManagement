using Microsoft.AspNetCore.Mvc;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;
using StudentMangement.Services;

namespace StudentMangement.Controllers
{
    public class StudentController : Controller
    {
        private readonly IStudentService _studentService;

        public StudentController(IStudentService studentService)
        {
            _studentService = studentService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public JsonResult GetStudentList()
        {
            var students = _studentService.GetStudents();
            return new JsonResult(students);
        }

        public IActionResult Create(int? id)
        {
            ViewBag.Student = null;
            if (id != null)
            {
                ViewBag.Student = _studentService.GetStudentById(id.Value);
            }
            return View();
        }

        [HttpPost]
        public IActionResult SaveStudent([FromBody] Student student)
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
                _studentService.SaveStudent(student);
                return Ok(true);
            }
            catch
            {
                return Ok(false);
            }
        }

        public bool DeleteStudent(int id)
        {
            try
            {
                _studentService.DeleteStudent(id);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public IActionResult EnrolledInMoreThan(int courseCount = 2)
        {
            var students = _studentService.GetStudentsEnrolledInMoreThan(courseCount);
            ViewBag.CourseCount = courseCount;
            return View(students);
        }
    }
}
