using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
        Mock<UserManager<ApplicationUser>>? userManager = null,
        Mock<ICourseService>? courseService = null) =>
        new(
            service.Object,
            (enrollmentService ?? new Mock<IEnrollmentService>()).Object,
            (courseService ?? new Mock<ICourseService>()).Object,
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

    private static void SetupTempData(StudentController controller) =>
        controller.TempData = new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>());

    private static Mock<UserManager<ApplicationUser>> LinkedUserManager(int studentId)
    {
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Linked Student", StudentId = studentId });
        return userManager;
    }

    [Fact]
    public async Task Enroll_UnlinkedAccount_RedirectsToMyCoursesWithoutSaving()
    {
        // Arrange — Student-role account with no student record link
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Unlinked Student" });
        var enrollmentService = new Mock<IEnrollmentService>();
        var controller = CreateController(new Mock<IStudentService>(), enrollmentService, userManager);
        SignInAs(controller, "unlinked@example.com");
        SetupTempData(controller);

        // Act
        var result = await controller.Enroll(3);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("MyCourses", redirect.ActionName);
        enrollmentService.Verify(s => s.SaveEnrollment(It.IsAny<Enrollment>()), Times.Never);
    }

    [Fact]
    public async Task Enroll_UnknownCourse_ReturnsNotFoundWithoutSaving()
    {
        // Arrange
        var courseService = new Mock<ICourseService>();
        courseService.Setup(s => s.GetCourseById(99)).ReturnsAsync((Course?)null);
        var enrollmentService = new Mock<IEnrollmentService>();
        var controller = CreateController(
            new Mock<IStudentService>(), enrollmentService, LinkedUserManager(7), courseService);
        SignInAs(controller, "linked@example.com");
        SetupTempData(controller);

        // Act
        var result = await controller.Enroll(99);

        // Assert
        Assert.IsType<NotFoundResult>(result);
        enrollmentService.Verify(s => s.SaveEnrollment(It.IsAny<Enrollment>()), Times.Never);
    }

    [Fact]
    public async Task Enroll_AlreadyEnrolled_RedirectsToCourseIndexWithError()
    {
        // Arrange
        var courseService = new Mock<ICourseService>();
        courseService.Setup(s => s.GetCourseById(3))
            .ReturnsAsync(new Course { Id = 3, Name = "Physics", Credits = 4 });
        var enrollmentService = new Mock<IEnrollmentService>();
        enrollmentService.Setup(s => s.IsStudentEnrolledInCourseAsync(7, 3)).ReturnsAsync(true);
        var controller = CreateController(
            new Mock<IStudentService>(), enrollmentService, LinkedUserManager(7), courseService);
        SignInAs(controller, "linked@example.com");
        SetupTempData(controller);

        // Act
        var result = await controller.Enroll(3);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Course", redirect.ControllerName);
        Assert.NotNull(controller.TempData["Error"]);
        enrollmentService.Verify(s => s.SaveEnrollment(It.IsAny<Enrollment>()), Times.Never);
    }

    [Fact]
    public async Task Enroll_CourseFull_RedirectsToCourseIndexWithServiceMessage()
    {
        // Arrange — the service enforces the admin-set capacity and throws
        var courseService = new Mock<ICourseService>();
        courseService.Setup(s => s.GetCourseById(3))
            .ReturnsAsync(new Course { Id = 3, Name = "Physics", Credits = 4, MaxEnrollments = 1 });
        var enrollmentService = new Mock<IEnrollmentService>();
        enrollmentService.Setup(s => s.IsStudentEnrolledInCourseAsync(7, 3)).ReturnsAsync(false);
        enrollmentService.Setup(s => s.SaveEnrollment(It.IsAny<Enrollment>()))
            .ThrowsAsync(new ValidationException("Course 'Physics' is full."));
        var controller = CreateController(
            new Mock<IStudentService>(), enrollmentService, LinkedUserManager(7), courseService);
        SignInAs(controller, "linked@example.com");
        SetupTempData(controller);

        // Act
        var result = await controller.Enroll(3);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Course", redirect.ControllerName);
        Assert.Equal("Course 'Physics' is full.", controller.TempData["Error"]);
    }

    [Fact]
    public async Task Enroll_LinkedUser_SavesEnrollmentWithAccountStudentIdAndRedirects()
    {
        // Arrange — the student id must come from the account link, never the request
        var courseService = new Mock<ICourseService>();
        courseService.Setup(s => s.GetCourseById(3))
            .ReturnsAsync(new Course { Id = 3, Name = "Physics", Credits = 4 });
        var enrollmentService = new Mock<IEnrollmentService>();
        enrollmentService.Setup(s => s.IsStudentEnrolledInCourseAsync(7, 3)).ReturnsAsync(false);
        enrollmentService.Setup(s => s.SaveEnrollment(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
        var controller = CreateController(
            new Mock<IStudentService>(), enrollmentService, LinkedUserManager(7), courseService);
        SignInAs(controller, "linked@example.com");
        SetupTempData(controller);

        // Act
        var result = await controller.Enroll(3);

        // Assert
        enrollmentService.Verify(s => s.SaveEnrollment(It.Is<Enrollment>(e =>
            e.Id == 0 && e.StudentId == 7 && e.CourseId == 3 && e.Grade == null)), Times.Once);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("MyCourses", redirect.ActionName);
        Assert.NotNull(controller.TempData["Success"]);
    }

    [Fact]
    public async Task Drop_UnlinkedAccount_RedirectsToMyCoursesWithoutDeleting()
    {
        // Arrange
        var userManager = CreateUserManager();
        userManager.Setup(manager => manager.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Unlinked Student" });
        var enrollmentService = new Mock<IEnrollmentService>();
        var controller = CreateController(new Mock<IStudentService>(), enrollmentService, userManager);
        SignInAs(controller, "unlinked@example.com");
        SetupTempData(controller);

        // Act
        var result = await controller.Drop(10);

        // Assert
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("MyCourses", redirect.ActionName);
        enrollmentService.Verify(s => s.DeleteEnrollment(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Drop_MissingEnrollment_ReturnsNotFoundWithoutDeleting()
    {
        // Arrange
        var enrollmentService = new Mock<IEnrollmentService>();
        enrollmentService.Setup(s => s.GetEnrollmentById(99)).ReturnsAsync((Enrollment?)null);
        var controller = CreateController(
            new Mock<IStudentService>(), enrollmentService, LinkedUserManager(7));
        SignInAs(controller, "linked@example.com");
        SetupTempData(controller);

        // Act
        var result = await controller.Drop(99);

        // Assert
        Assert.IsType<NotFoundResult>(result);
        enrollmentService.Verify(s => s.DeleteEnrollment(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Drop_AnotherStudentsEnrollment_ReturnsNotFoundWithoutDeleting()
    {
        // Arrange — enrollment 10 belongs to student 8, the account is linked to student 7
        var enrollmentService = new Mock<IEnrollmentService>();
        enrollmentService.Setup(s => s.GetEnrollmentById(10))
            .ReturnsAsync(new Enrollment { Id = 10, StudentId = 8, CourseId = 3 });
        var controller = CreateController(
            new Mock<IStudentService>(), enrollmentService, LinkedUserManager(7));
        SignInAs(controller, "linked@example.com");
        SetupTempData(controller);

        // Act
        var result = await controller.Drop(10);

        // Assert
        Assert.IsType<NotFoundResult>(result);
        enrollmentService.Verify(s => s.DeleteEnrollment(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Drop_OwnEnrollment_DeletesAndRedirectsToMyCourses()
    {
        // Arrange
        var enrollmentService = new Mock<IEnrollmentService>();
        enrollmentService.Setup(s => s.GetEnrollmentById(10))
            .ReturnsAsync(new Enrollment { Id = 10, StudentId = 7, CourseId = 3 });
        enrollmentService.Setup(s => s.DeleteEnrollment(10)).Returns(Task.CompletedTask);
        var controller = CreateController(
            new Mock<IStudentService>(), enrollmentService, LinkedUserManager(7));
        SignInAs(controller, "linked@example.com");
        SetupTempData(controller);

        // Act
        var result = await controller.Drop(10);

        // Assert
        enrollmentService.Verify(s => s.DeleteEnrollment(10), Times.Once);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("MyCourses", redirect.ActionName);
        Assert.NotNull(controller.TempData["Success"]);
    }
}
