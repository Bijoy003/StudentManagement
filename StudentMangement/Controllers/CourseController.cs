using Microsoft.AspNetCore.Mvc;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;
using StudentMangement.Services;

namespace StudentMangement.Controllers
{
    public class CourseController : Controller
    {
        private readonly ICourseService _courseService;

        public CourseController(ICourseService service)
        {
            _courseService = service;
        }

        public IActionResult Index()
        {
            var courses = _courseService.GetCourses();
            return View(courses);
        }

        public IActionResult Create(int? id)
        {
            Course course;

            if (id == null)
            {
                course = new Course();
            }
            else
            {
                course = _courseService.GetCourseById(id.Value);
            }

            return View(course);
        }

        [HttpPost]
        public IActionResult Save([FromBody] Course course)
        {
            try
            {
                _courseService.SaveCourse(course);
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
                _courseService.DeleteCourse(id);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public IActionResult StudentCountPerCourse()
        {
            var data = _courseService.GetStudentCountPerCourse();
            return View(data);
        }

    }
}
