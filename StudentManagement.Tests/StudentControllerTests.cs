using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Web.Controllers;

namespace StudentManagement.Tests;

// REWORK (contract) — pins the ValidationException contract on the MVC surface:
// StudentService's Chain-of-Responsibility failures must reach the browser through
// the same 400 { Message } shape as ModelState errors, not through the generic 500.
public class StudentControllerTests
{
    private static StudentController CreateController(Mock<IStudentService> service) =>
        new(service.Object, NullLogger<StudentController>.Instance);

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
}
