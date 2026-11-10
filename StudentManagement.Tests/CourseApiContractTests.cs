using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.DTOs.Courses;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Web.Controllers;

namespace StudentManagement.Tests;

// WP7 AC3 — GradeMode exposed through /api/courses DTOs only (create/update/read).
// REST never binds or returns the Course entity. AC4's UI dropdown is e2e-covered;
// only these DTO/controller-level tests apply.
public class CourseApiContractTests
{
    private static Course CreateCourse(int id, string name, GradeMode mode)
    {
        var course = new Course { Id = id, Name = name, Credits = 3 };
        var property = typeof(Course).GetProperty("GradeMode");
        Assert.NotNull(property); // RED until WP7 AC1 lands the property
        property!.SetValue(course, mode);
        return course;
    }

    private static GradeMode ReadGradeMode(Course course)
    {
        var property = typeof(Course).GetProperty("GradeMode");
        Assert.NotNull(property);
        return (GradeMode)property!.GetValue(course)!;
    }

    private static CourseApiController CreateController(Mock<ICourseService> service)
    {
        return new CourseApiController(service.Object, NullLogger<CourseApiController>.Instance);
    }

    // ---------- DTO shape (guards — must stay true) ----------

    [Fact]
    public void CourseDto_GradeModeMember_ReturnsGradeModeTyped()
    {
        // Arrange / Act
        var property = typeof(CourseDto).GetProperty("GradeMode");

        // Assert
        Assert.NotNull(property);
        Assert.Same(typeof(GradeMode), property!.PropertyType);
    }

    [Fact]
    public void CourseRequest_GradeModeProperty_ReturnsLetterDefault()
    {
        // Arrange / Act
        var property = typeof(CourseRequest).GetProperty("GradeMode");

        // Assert
        Assert.NotNull(property);
        Assert.Same(typeof(GradeMode), property!.PropertyType);
        Assert.Equal(GradeMode.Letter, (GradeMode)property.GetValue(new CourseRequest())!);
    }

    [Fact]
    public void CourseApiController_Route_ReturnsApiCourses()
    {
        // Arrange / Act
        var route = typeof(CourseApiController).GetCustomAttribute<RouteAttribute>();

        // Assert
        Assert.NotNull(route);
        Assert.Equal("api/courses", route!.Template);
        Assert.NotNull(typeof(CourseApiController).GetCustomAttribute<ApiControllerAttribute>());
    }

    // ---------- authorization contract (writes gated, reads any-authenticated) ----------

    [Fact]
    public void CourseApiController_WriteActions_ReturnsAdminTeacherRoleGate()
    {
        // Arrange — mirror of StudentApiController: writes are Admin/Teacher-only
        var writeActions = new[] { "CreateCourse", "UpdateCourse", "DeleteCourse" };

        foreach (var name in writeActions)
        {
            // Act
            var action = typeof(CourseApiController).GetMethod(name);
            Assert.NotNull(action);
            var authorize = action!.GetCustomAttribute<AuthorizeAttribute>();

            // Assert
            Assert.NotNull(authorize);
            Assert.Equal("Admin,Teacher", authorize!.Roles);
        }
    }

    [Fact]
    public void CourseApiController_ReadActions_ReturnsNoMethodLevelRoleGate()
    {
        // Arrange — GETs stay any-authenticated under the class-level [Authorize]
        var classAuthorize = typeof(CourseApiController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(classAuthorize);
        Assert.Null(classAuthorize!.Roles);

        foreach (var name in new[] { "GetCourses", "GetCourseById" })
        {
            // Act
            var action = typeof(CourseApiController).GetMethod(name);

            // Assert — no action-level [Authorize] override on the reads
            Assert.NotNull(action);
            Assert.Null(action!.GetCustomAttribute<AuthorizeAttribute>());
        }
    }

    [Fact]
    public void CourseApiController_ActionParameters_ReturnsNoCourseEntityBinding()
    {
        // Arrange — every public action declared on the api/courses controller
        var actions = typeof(CourseApiController)
            .GetTypeInfo()
            .DeclaredMethods
            .Where(m => m.IsPublic && !m.IsSpecialName)
            .ToList();

        Assert.NotEmpty(actions);

        // Assert — REST never binds or serializes domain entities (AGENTS rule 2)
        foreach (var action in actions)
        {
            foreach (var parameter in action.GetParameters())
            {
                Assert.NotSame(typeof(Course), parameter.ParameterType);
                Assert.DoesNotContain(typeof(Course), GetGenericArgumentsRecursive(parameter.ParameterType));
            }
        }
    }

    private static IEnumerable<Type> GetGenericArgumentsRecursive(Type type)
    {
        var seen = new List<Type>();
        var current = type;
        while (current != null && current != typeof(object) && current.IsGenericType)
        {
            seen.AddRange(current.GetGenericArguments());
            current = current.GetGenericArguments().FirstOrDefault(a => a.IsGenericType);
        }

        return seen;
    }

    // ---------- mapping (RED: CourseMappingExtensions stubs throw) ----------

    [Fact]
    public void CourseMappingExtensions_ToDto_GpaCourse_ReturnsDtoWithGpaMode()
    {
        // Arrange
        var course = CreateCourse(7, "Seminar", GradeMode.Gpa);
        course.TeacherId = "teacher-1";

        // Act
        var dto = course.ToDto();

        // Assert
        Assert.Equal(7, dto.Id);
        Assert.Equal("Seminar", dto.Name);
        Assert.Equal("teacher-1", dto.TeacherId);
        Assert.Equal(GradeMode.Gpa, dto.GradeMode);
    }

    [Fact]
    public void CourseMappingExtensions_ToEntity_PassFailRequest_ReturnsNewCourseWithPassFailMode()
    {
        // Arrange
        var request = new CourseRequest { Name = "Lab", Credits = 4, GradeMode = GradeMode.PassFail };

        // Act
        var course = request.ToEntity();

        // Assert — create path: never carries an identity
        Assert.Equal(0, course.Id);
        Assert.Equal("Lab", course.Name);
        Assert.Equal(4, course.Credits);
        Assert.Equal(GradeMode.PassFail, ReadGradeMode(course));
    }

    [Fact]
    public void CourseMappingExtensions_ApplyTo_GpaRequest_ReturnsGradeModeUpdatedTeacherIdKept()
    {
        // Arrange
        var course = CreateCourse(3, "Old Name", GradeMode.Letter);
        course.TeacherId = "teacher-keep";
        var request = new CourseRequest { Name = "New Name", Credits = 5, GradeMode = GradeMode.Gpa };

        // Act
        request.ApplyTo(course);

        // Assert — TeacherId is never bound through the DTO
        Assert.Equal(3, course.Id);
        Assert.Equal("New Name", course.Name);
        Assert.Equal(5, course.Credits);
        Assert.Equal(GradeMode.Gpa, ReadGradeMode(course));
        Assert.Equal("teacher-keep", course.TeacherId);
    }

    // ---------- controller behavior (RED: CourseApiController stubs throw) ----------

    [Fact]
    public async Task CourseApiController_GetCourses_ReturnsDtosExposingGradeMode()
    {
        // Arrange
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetAllCourses())
               .ReturnsAsync(new List<Course> { CreateCourse(1, "A", GradeMode.Gpa), CreateCourse(2, "B", GradeMode.Letter) });
        var controller = CreateController(service);

        // Act
        var result = await controller.GetCourses();

        // Assert — payload is DTOs, not entities
        Assert.IsAssignableFrom<IEnumerable<CourseDto>>(result.Value);
        var dtos = result.Value!.ToList();
        Assert.Equal(2, dtos.Count);
        Assert.Equal(GradeMode.Gpa, dtos.Single(d => d.Id == 1).GradeMode);
    }

    [Fact]
    public async Task CourseApiController_GetCourseById_ExistingCourse_ReturnsDtoWithGradeMode()
    {
        // Arrange
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetCourseById(9)).ReturnsAsync(CreateCourse(9, "Physics", GradeMode.PassFail));
        var controller = CreateController(service);

        // Act
        var result = await controller.GetCourseById(9);

        // Assert
        Assert.NotNull(result.Value);
        Assert.Equal(GradeMode.PassFail, result.Value!.GradeMode);
        Assert.Equal("Physics", result.Value.Name);
    }

    [Fact]
    public async Task CourseApiController_CreateCourse_PassFailRequest_ReturnsCourseSavedWithPassFailMode()
    {
        // Arrange
        var saved = new List<Course>();
        var service = new Mock<ICourseService>();
        service.Setup(s => s.SaveCourse(It.IsAny<Course>()))
               .Callback<Course>(c => saved.Add(c))
               .Returns(Task.CompletedTask);
        var controller = CreateController(service);
        var request = new CourseRequest { Name = "New Lab", Credits = 2, GradeMode = GradeMode.PassFail };

        // Act
        var result = await controller.CreateCourse(request);

        // Assert — DTO was mapped into a Course before the service call
        var course = Assert.Single(saved);
        Assert.Equal(GradeMode.PassFail, ReadGradeMode(course));
        Assert.Equal("New Lab", course.Name);
        var payload = result.Value ?? (result.Result as ObjectResult)?.Value;
        var dto = Assert.IsType<CourseDto>(payload);
        Assert.Equal(GradeMode.PassFail, dto.GradeMode);
    }

    [Fact]
    public async Task CourseApiController_UpdateCourse_GpaRequest_ReturnsGradeModeAppliedToPersistedCourse()
    {
        // Arrange
        var persisted = CreateCourse(4, "Algebra", GradeMode.Letter);
        persisted.TeacherId = "teacher-x";
        var saved = new List<Course>();
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetCourseById(4)).ReturnsAsync(persisted);
        service.Setup(s => s.SaveCourse(It.IsAny<Course>()))
               .Callback<Course>(c => saved.Add(c))
               .Returns(Task.CompletedTask);
        var controller = CreateController(service);
        var request = new CourseRequest { Name = "Algebra II", Credits = 3, GradeMode = GradeMode.Gpa };

        // Act
        var result = await controller.UpdateCourse(4, request);

        // Assert — update mutates the loaded entity (identity + ownership kept) and returns 204
        var course = Assert.Single(saved);
        Assert.Equal(4, course.Id);
        Assert.Equal("teacher-x", course.TeacherId);
        Assert.Equal(GradeMode.Gpa, ReadGradeMode(course));
        Assert.IsType<NoContentResult>(result);
    }
}
