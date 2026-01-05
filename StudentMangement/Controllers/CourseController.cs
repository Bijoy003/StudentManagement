using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace StudentManagement.Web.Controllers
{
    [Authorize]
    public class CourseController : Controller
    {
        private readonly ICourseService _courseService;
        private readonly ILogger<CourseController> _logger;

        public CourseController(ICourseService service, ILogger<CourseController> logger)
        {
            _courseService = service;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                var courses = await _courseService.GetAllCourses();
                return View(courses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }

        public async Task<IActionResult> Create(int? id)
        {
            try
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
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
                await _courseService.DeleteCourse(id);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return false;
            }
        }

        public async Task<IActionResult> StudentCountPerCourse()
        {
            try
            {
                var data = await _courseService.GetStudentCountPerCourse();
                return View(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }
    }
}
