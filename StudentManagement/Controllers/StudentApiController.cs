using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using StudentManagement.Application.DTOs.Common;
using StudentManagement.Application.DTOs.Students;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Web.Controllers;

[ApiController]
[Route("api/students")]
[Authorize]
[Produces("application/json")]
public sealed class StudentApiController : ControllerBase
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    private readonly IStudentService _studentService;
    private readonly ILogger<StudentApiController> _logger;

    public StudentApiController(IStudentService studentService, ILogger<StudentApiController> logger)
    {
        _studentService = studentService;
        _logger = logger;
    }

    // GET api/students?page=1&pageSize=25&search=ada&minCourses=2
    [HttpGet(Name = "ApiGetStudents")]
    [ProducesResponseType(typeof(PagedResult<StudentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<PagedResult<StudentDto>>> GetStudents([FromQuery] StudentQuery query)
    {
        try
        {
            var page = query.Page < 1 ? 1 : query.Page;
            var pageSize = query.PageSize < 1
                ? DefaultPageSize
                : Math.Min(query.PageSize, MaxPageSize);

            IEnumerable<Student> students = query.MinCourses is > 0
                ? await _studentService.GetStudentsEnrolledInMoreThan(query.MinCourses.Value)
                : await _studentService.GetStudentsAsync();

            if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var term = query.Search.Trim();
                students = students.Where(s =>
                    (s.Name?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (s.Email?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var ordered = students.OrderBy(s => s.Id).ToList();
            var totalCount = ordered.Count;

            var items = ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => s.ToDto())
                .ToList();

            Response.Headers.Append("X-Total-Count", totalCount.ToString(CultureInfo.InvariantCulture));

            return Ok(new PagedResult<StudentDto>(items, page, pageSize, totalCount));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while listing students");
            return UnexpectedError("An unexpected error occurred while retrieving students.");
        }
    }

    // GET api/students/5
    [HttpGet("{id:int}", Name = "ApiGetStudentById")]
    [ProducesResponseType(typeof(StudentDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StudentDto>> GetStudentById(int id)
    {
        try
        {
            var student = await _studentService.GetStudentByIdAsync(id);
            if (student is null)
            {
                return StudentNotFound(id);
            }

            return Ok(student.ToDto());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while retrieving student {StudentId}", id);
            return UnexpectedError("An unexpected error occurred while retrieving the student.");
        }
    }

    // POST api/students
    [HttpPost(Name = "ApiCreateStudent")]
    [Authorize(Roles = "Admin,Teacher")]
    [ProducesResponseType(typeof(StudentDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StudentDto>> CreateStudent([FromBody] StudentRequest request)
    {
        try
        {
            var student = request.ToEntity();
            await _studentService.SaveStudentAsync(student);

            var dto = student.ToDto();
            return CreatedAtRoute("ApiGetStudentById", new { id = dto.Id }, dto);
        }
        catch (ValidationException ex)
        {
            // Chain-of-Responsibility validation failure (StudentService): declared 400
            // contract, ValidationProblemDetails carrying the failing rule's message.
            _logger.LogWarning(ex, "Validation failed while creating student {StudentName}", request.Name);
            return ValidationError(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while creating student {StudentName}", request.Name);
            return UnexpectedError("An unexpected error occurred while creating the student.");
        }
    }

    // PUT api/students/5
    [HttpPut("{id:int}", Name = "ApiUpdateStudent")]
    [Authorize(Roles = "Admin,Teacher")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStudent(int id, [FromBody] StudentRequest request)
    {
        try
        {
            var student = await _studentService.GetStudentByIdAsync(id);
            if (student is null)
            {
                return StudentNotFound(id);
            }

            request.ApplyTo(student);
            await _studentService.SaveStudentAsync(student);

            return NoContent();
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed while updating student {StudentId}", id);
            return ValidationError(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while updating student {StudentId}", id);
            return UnexpectedError("An unexpected error occurred while updating the student.");
        }
    }

    // PATCH api/students/5  (application/merge-patch+json)
    [HttpPatch("{id:int}", Name = "ApiPatchStudent")]
    [Authorize(Roles = "Admin,Teacher")]
    [Consumes("application/json", "application/merge-patch+json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status415UnsupportedMediaType)]
    public async Task<IActionResult> PatchStudent(int id, [FromBody] StudentPatchRequest request)
    {
        try
        {
            var student = await _studentService.GetStudentByIdAsync(id);
            if (student is null)
            {
                return StudentNotFound(id);
            }

            request.ApplyTo(student);
            await _studentService.SaveStudentAsync(student);

            return NoContent();
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning(ex, "Validation failed while patching student {StudentId}", id);
            return ValidationError(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while patching student {StudentId}", id);
            return UnexpectedError("An unexpected error occurred while updating the student.");
        }
    }

    // DELETE api/students/5
    [HttpDelete("{id:int}", Name = "ApiDeleteStudent")]
    [Authorize(Roles = "Admin,Teacher")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteStudent(int id)
    {
        try
        {
            var student = await _studentService.GetStudentByIdAsync(id);
            if (student is null)
            {
                return StudentNotFound(id);
            }

            await _studentService.DeleteStudentAsync(id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while deleting student {StudentId}", id);
            return UnexpectedError("An unexpected error occurred while deleting the student.");
        }
    }

    private ObjectResult StudentNotFound(int id) =>
        Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Student not found",
            detail: $"No student with id {id} exists.");

    // 400 matching the [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    // contract of the write actions: message exposed as Detail AND as Errors[""].
    private ObjectResult ValidationError(string message)
    {
        var modelState = new ModelStateDictionary();
        modelState.AddModelError(string.Empty, message);

        return new ObjectResult(new ValidationProblemDetails(modelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Validation failed",
            Detail = message
        })
        {
            StatusCode = StatusCodes.Status400BadRequest,
            DeclaredType = typeof(ValidationProblemDetails)
        };
    }

    private ObjectResult UnexpectedError(string detail) =>
        Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Unexpected error",
            detail: detail);
}
