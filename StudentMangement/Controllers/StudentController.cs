using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;

namespace StudentMangement.Controllers
{
    [Authorize]
    public class StudentController : Controller
    {
        private readonly IStudentService _studentService;
        private readonly ILogger<StudentController> _logger;

        public StudentController(IStudentService studentService, ILogger<StudentController> logger)
        {
            _studentService = studentService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<JsonResult> GetStudentList()
        {
            var students = await _studentService.GetStudentsAsync();
            return new JsonResult(students);
        }

        public async Task<IActionResult> Create(int? id)
        {
            ViewBag.Student = null;
            if (id != null)
            {
                ViewBag.Student = await _studentService.GetStudentByIdAsync(id.Value);
            }
            return View();
        }

        [HttpPost]
        //[ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveStudent([FromBody] Student student)
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
                await _studentService.SaveStudentAsync(student);
                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while saving student: {StudentName}", student?.Name);
                return StatusCode(500, new { Message = "An unexpected error occurred." });
            }
        }

        public async Task<IActionResult> DeleteStudent(int id)
        {
            try
            {
                await _studentService.DeleteStudentAsync(id);
                return Ok(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while deleting student: {Id}", id);
                return StatusCode(500, new { Message = "An unexpected error occurred." });
            }
        }

        public async Task<IActionResult> EnrolledInMoreThan(int courseCount = 2)
        {
            try
            {
                var students = await _studentService.GetStudentsEnrolledInMoreThan(courseCount);
                ViewBag.CourseCount = courseCount;
                return View(students);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred.");
                return View("Error");
            }
        }
    }
}
