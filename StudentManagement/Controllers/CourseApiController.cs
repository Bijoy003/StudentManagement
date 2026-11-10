using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentManagement.Application.DTOs.Courses;

namespace StudentManagement.Web.Controllers;

// REST surface for courses: binds/returns CourseDto / CourseRequest ONLY — never the
// Course entity (AGENTS rule 2). Create/update flow through CourseMappingExtensions
// into ICourseService.SaveCourse, with the same status-code style AND the same authz
// split as StudentApiController: reads any-authenticated under the class-level
// [Authorize], writes gated to Admin/Teacher on the action.
[ApiController]
[Route("api/courses")]
[Authorize]
[Produces("application/json")]
public sealed class CourseApiController : ControllerBase
{
    private readonly ICourseService _courseService;
    private readonly ILogger<CourseApiController> _logger;

    public CourseApiController(ICourseService courseService, ILogger<CourseApiController> logger)
    {
        _courseService = courseService;
        _logger = logger;
    }

    // GET api/courses
    [HttpGet(Name = "ApiGetCourses")]
    [ProducesResponseType(typeof(IEnumerable<CourseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<IEnumerable<CourseDto>>> GetCourses()
    {
        try
        {
            var courses = await _courseService.GetAllCourses();
            return courses.Select(course => course.ToDto()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while listing courses");
            return UnexpectedError("An unexpected error occurred while retrieving courses.");
        }
    }

    // GET api/courses/5
    [HttpGet("{id:int}", Name = "ApiGetCourseById")]
    [ProducesResponseType(typeof(CourseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseDto>> GetCourseById(int id)
    {
        try
        {
            var course = await _courseService.GetCourseById(id);
            if (course is null)
            {
                return CourseNotFound(id);
            }

            return course.ToDto();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while retrieving course {CourseId}", id);
            return UnexpectedError("An unexpected error occurred while retrieving the course.");
        }
    }

    // POST api/courses
    [HttpPost(Name = "ApiCreateCourse")]
    [Authorize(Roles = "Admin,Teacher")]
    [ProducesResponseType(typeof(CourseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CourseDto>> CreateCourse([FromBody] CourseRequest request)
    {
        try
        {
            var course = request.ToEntity();
            await _courseService.SaveCourse(course);

            var dto = course.ToDto();
            return CreatedAtRoute("ApiGetCourseById", new { id = dto.Id }, dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while creating course {CourseName}", request.Name);
            return UnexpectedError("An unexpected error occurred while creating the course.");
        }
    }

    // PUT api/courses/5
    [HttpPut("{id:int}", Name = "ApiUpdateCourse")]
    [Authorize(Roles = "Admin,Teacher")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCourse(int id, [FromBody] CourseRequest request)
    {
        try
        {
            var course = await _courseService.GetCourseById(id);
            if (course is null)
            {
                return CourseNotFound(id);
            }

            request.ApplyTo(course);
            await _courseService.SaveCourse(course);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while updating course {CourseId}", id);
            return UnexpectedError("An unexpected error occurred while updating the course.");
        }
    }

    // DELETE api/courses/5
    [HttpDelete("{id:int}", Name = "ApiDeleteCourse")]
    [Authorize(Roles = "Admin,Teacher")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteCourse(int id)
    {
        try
        {
            var course = await _courseService.GetCourseById(id);
            if (course is null)
            {
                return CourseNotFound(id);
            }

            await _courseService.DeleteCourse(id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while deleting course {CourseId}", id);
            return UnexpectedError("An unexpected error occurred while deleting the course.");
        }
    }

    private ObjectResult CourseNotFound(int id) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Course not found",
            detail: $"No course with id {id} exists.");

    private ObjectResult UnexpectedError(string detail) =>
        Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unexpected error",
            detail: detail);
}
