using Microsoft.AspNetCore.Mvc;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;

namespace StudentMangement.Controllers
{
    public class CourseController : Controller
    {
        private readonly ICourseService _service;

        public CourseController(ICourseService service)
        {
            _service = service;
        }

        public IActionResult Index()
        {
            var courses = _service.GetCourses();
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
                course = _service.GetCourseById(id.Value);
            }

            return View(course);
        }

        [HttpPost]
        public IActionResult Save([FromBody] Course course)
        {
            try
            {
                _service.SaveCourse(course);
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
                _service.DeleteCourse(id);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
