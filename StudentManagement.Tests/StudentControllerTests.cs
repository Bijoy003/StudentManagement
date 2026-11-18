using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Web.Controllers;

namespace StudentManagement.Tests;

// REWORK (contract) — pins the ValidationException contract on the MVC surface:
// StudentService's Chain-of-Responsibility failures must reach the browser through
// the same 400 { Message } shape as ModelState errors, not through the generic 500.
public class StudentControllerTests
{
    private static Mock<UserManager<ApplicationUser>> CreateUserManager() =>
        new(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    private static StudentController CreateController(
        Mock<IStudentService> service,
        Mock<IEnrollmentService>? enrollmentService = null,
        Mock<UserManager<ApplicationUser>>? userManager = null) =>
        new(
            service.Object,
            (enrollmentService ?? new Mock<IEnrollmentService>()).Object,
            (userManager ?? CreateUserManager()).Object,
            NullLogger<StudentController>.Instance);

    private static void SignInAs(StudentController controller, string email)
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.Name, email)], "TestAuth"))
            }
        };
    }

    private static string? ReadMessage(object? value) =>
        value?.GetType().GetProperty("Message")?.GetValue(value) as string;

    [Fact]
    public async Task SaveStudent_ServiceThrowsValidationException_Returns400BadRequestWithMessage()
    {
        // Arrange — the validation chain (RequiredFieldsRule -> ...) rejects inside StudentService
        var mock = new Mock<IStudentService>();
        mock.Setup(s => s.SaveStudentAsync(It.IsAny<Student>()))
            .ThrowsAsync(new ValidationException("Name is required."));
        var controller = CreateController(mock);

        // Act
        var result = await controller.SaveStudent(new Student { Name = "", Email = "ada@example.com" });

        // Assert — existing MVC 400 shape, message carried from the failing rule
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("Name is required.", ReadMessage(badRequest.Value));
    }

    [Fact]
    public async Task SaveStudent_ServiceThrowsUnexpectedException_Returns500WithGenericMessage()
    {
        // Arrange — the new ValidationException catch must not swallow other failures
        var mock = new Mock<IStudentService>();
        mock.Setup(s => s.SaveStudentAsync(It.IsAny<Student>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        var controller = CreateController(mock);

        // Act
        var result = await controller.SaveStudent(new Student { Name = "Ada", Email = "ada@example.com" });

        // Assert — the generic catch still owns non-validation errors
        var statusCode = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, statusCode.StatusCode);
        Assert.Equal("An unexpected error occurred.", ReadMessage(statusCode.Value));
    }

    [Fact]
    public async Task MyCourses_UserWithoutStudentLink_ReturnsNotLinkedView()
    {
        // Arrange — a Student-role account that no admin has linked to a student row
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Unlinked Student" });
        var controller = CreateController(new Mock<IStudentService>(), userManager: userManager);
        SignInAs(controller, "unlinked@example.com");

        // Act
        var result = await controller.MyCourses();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal("MyCoursesNotLinked", view.ViewName);
    }

    [Fact]
    public async Task MyCourses_LinkedUser_ReturnsOnlyOwnEnrollments()
    {
        // Arrange — the service returns everyone's enrollments; the controller must filter
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Linked Student", StudentId = 7 });

        var enrollmentService = new Mock<IEnrollmentService>();
        enrollmentService.Setup(service => service.GetEnrollments())
            .ReturnsAsync(new List<Enrollment>
            {
                new() { Id = 1, StudentId = 7, CourseId = 1, Course = new Course { Id = 1, Name = "Math" } },
                new() { Id = 2, StudentId = 8, CourseId = 2, Course = new Course { Id = 2, Name = "Physics" } }
            });

        var controller = CreateController(
            new Mock<IStudentService>(),
            enrollmentService,
            userManager);
        SignInAs(controller, "linked@example.com");

        // Act
        var result = await controller.MyCourses();

        // Assert
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Enrollment>>(view.Model).ToList();
        var enrollment = Assert.Single(model);
        Assert.Equal("Math", enrollment.Course.Name);
        Assert.Equal("Linked Student", view.ViewData["StudentName"]);
    }
}
