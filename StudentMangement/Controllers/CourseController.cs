using Microsoft.AspNetCore.Mvc;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;

namespace StudentMangement.Controllers
{
    public class CourseController : Controller
    {
        private readonly ICourseService _courseService;

        public CourseController(ICourseService service)
        {
            _courseService = service;
        }

        public async Task<IActionResult> Index()
        {
            var courses = await _courseService.GetAllCourses();
            return View(courses);
        }

        public async Task<IActionResult> Create(int? id)
        {
            Course course;

            if (id == null)
            {
                course = new Course();
            }
            else
            {
                course = await _courseService.GetCourseById(id.Value);
            }

            return View(course);
        }

        [HttpPost]
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
                await _courseService.SaveCourse(course);
                return Ok(true);
            }
            catch
            {
                return Ok(false);
            }
        }

        public async Task<bool> Delete(int id)
        {
            try
            {
                await _courseService.DeleteCourse(id);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<IActionResult> StudentCountPerCourse()
        {
            var data = await _courseService.GetStudentCountPerCourse();
            return View(data);
        }
    }
}
