using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.DTOs.Common;
using StudentManagement.Application.DTOs.Students;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Web.Controllers;

namespace StudentManagement.Tests;

public class StudentApiControllerTests
{
    private sealed class TestProblemDetailsFactory : ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(
            HttpContext httpContext,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null) => new()
        {
            Status = statusCode,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance
        };

        public override ValidationProblemDetails CreateValidationProblemDetails(
            HttpContext httpContext,
            ModelStateDictionary modelState,
            int? statusCode = null,
            string? title = null,
            string? type = null,
            string? detail = null,
            string? instance = null) => new(modelState)
        {
            Status = statusCode,
            Title = title,
            Type = type,
            Detail = detail,
            Instance = instance
        };
    }

    private static StudentApiController CreateController(Mock<IStudentService> service)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ProblemDetailsFactory, TestProblemDetailsFactory>();

        var controller = new StudentApiController(service.Object, NullLogger<StudentApiController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    RequestServices = services.BuildServiceProvider()
                }
            }
        };

        return controller;
    }

    private static Mock<IStudentService> CreateServiceMock() => new(MockBehavior.Strict);

    [Fact]
    public async Task GetStudents_NoQuery_ReturnsPagedResultAndTotalCountHeader()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentsAsync()).ReturnsAsync(new List<Student>
        {
            new() { Id = 1, Name = "Ada Lovelace", Email = "ada@example.com" },
            new() { Id = 2, Name = "Bob Stone", Email = "bob@example.com" }
        });

        var controller = CreateController(mock);

        // Act
        var result = await controller.GetStudents(new StudentQuery());

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PagedResult<StudentDto>>(ok.Value);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(1, page.Page);
        Assert.Equal(25, page.PageSize);
        Assert.Equal("2", controller.Response.Headers["X-Total-Count"].ToString());
    }

    [Fact]
    public async Task GetStudents_WithSearch_FiltersByNameOrEmail()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentsAsync()).ReturnsAsync(new List<Student>
        {
            new() { Id = 1, Name = "Ada Lovelace", Email = "ada@example.com" },
            new() { Id = 2, Name = "Bob Stone", Email = "bob@example.com" },
            new() { Id = 3, Name = "Grace Hopper", Email = "ADA.h@navy.mil" }
        });

        var controller = CreateController(mock);

        // Act
        var result = await controller.GetStudents(new StudentQuery { Search = "ada" });

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PagedResult<StudentDto>>(ok.Value);
        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, item => Assert.Contains("ada", (item.Name + item.Email)!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetStudents_WithPaging_ReturnsRequestedSliceOfResults()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentsAsync()).ReturnsAsync(Enumerable.Range(1, 10)
            .Select(i => new Student { Id = i, Name = $"Student {i}", Email = $"s{i}@example.com" })
            .ToList());

        var controller = CreateController(mock);

        // Act
        var result = await controller.GetStudents(new StudentQuery { Page = 3, PageSize = 4 });

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PagedResult<StudentDto>>(ok.Value);
        Assert.Equal(10, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(new[] { 9, 10 }, page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task GetStudents_PageSizeAboveMaximum_IsClamped()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentsAsync()).ReturnsAsync(new List<Student>());

        var controller = CreateController(mock);

        // Act
        var result = await controller.GetStudents(new StudentQuery { Page = 0, PageSize = 5_000 });

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PagedResult<StudentDto>>(ok.Value);
        Assert.Equal(1, page.Page);
        Assert.Equal(100, page.PageSize);
    }

    [Fact]
    public async Task GetStudents_WithMinCourses_UsesEnrollmentQuery()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentsEnrolledInMoreThan(3)).ReturnsAsync(new List<Student>
        {
            new() { Id = 7, Name = "Carol Danvers", Email = "carol@example.com" }
        });

        var controller = CreateController(mock);

        // Act
        var result = await controller.GetStudents(new StudentQuery { MinCourses = 3 });

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var page = Assert.IsType<PagedResult<StudentDto>>(ok.Value);
        Assert.Single(page.Items);
        Assert.Equal(7, page.Items[0].Id);
        mock.Verify(s => s.GetStudentsAsync(), Times.Never);
    }

    [Fact]
    public async Task GetStudents_ServiceThrows_Returns500ProblemDetails()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentsAsync()).ThrowsAsync(new InvalidOperationException("boom"));

        var controller = CreateController(mock);

        // Act
        var result = await controller.GetStudents(new StudentQuery());

        // Assert
        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, problem.StatusCode);
        Assert.IsType<ProblemDetails>(problem.Value);
    }

    [Fact]
    public async Task GetStudentById_ExistingId_ReturnsOkWithStudent()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentByIdAsync(42)).ReturnsAsync(new Student
        {
            Id = 42,
            Name = "Ada Lovelace",
            Email = "ada@example.com",
            Phone = "555-0100",
            Address = "1 Analytical Engine Way",
            DateOfEnroll = new DateTime(2024, 9, 1)
        });

        var controller = CreateController(mock);

        // Act
        var result = await controller.GetStudentById(42);

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var dto = Assert.IsType<StudentDto>(ok.Value);
        Assert.Equal(42, dto.Id);
        Assert.Equal("Ada Lovelace", dto.Name);
        Assert.Equal(new DateTime(2024, 9, 1), dto.DateOfEnroll);
    }

    [Fact]
    public async Task GetStudentById_UnknownId_Returns404ProblemDetails()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentByIdAsync(1014)).Returns(Task.FromResult<Student>(null!));

        var controller = CreateController(mock);

        // Act
        var result = await controller.GetStudentById(1014);

        // Assert
        var problem = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(404, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Contains("1014", details.Detail);
    }

    [Fact]
    public async Task CreateStudent_ValidRequest_Returns201WithLocation()
    {
        // Arrange
        Student? created = null;
        var mock = CreateServiceMock();
        mock.Setup(s => s.SaveStudentAsync(It.IsAny<Student>()))
            .Callback<Student>(student =>
            {
                student.Id = 1014;
                created = student;
            })
            .Returns(Task.CompletedTask);

        var controller = CreateController(mock);

        // Act
        var result = await controller.CreateStudent(new StudentRequest
        {
            Name = "  Ada Lovelace  ",
            Email = "  ada@example.com ",
            Phone = "555-0100"
        });

        // Assert
        Assert.NotNull(created);
        Assert.Equal("Ada Lovelace", created!.Name);
        Assert.Equal("ada@example.com", created.Email);

        var createdAt = Assert.IsType<CreatedAtRouteResult>(result.Result);
        Assert.Equal("ApiGetStudentById", createdAt.RouteName);
        Assert.Equal(1014, createdAt.RouteValues!["id"]);
        Assert.Equal(201, createdAt.StatusCode);

        var dto = Assert.IsType<StudentDto>(createdAt.Value);
        Assert.Equal(1014, dto.Id);
    }

    [Fact]
    public async Task UpdateStudent_ExistingId_ReplacesFieldsAndReturns204()
    {
        // Arrange
        var existing = new Student
        {
            Id = 5,
            Name = "Old Name",
            Email = "old@example.com",
            Phone = "555-0000",
            Address = "Old address"
        };

        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentByIdAsync(5)).ReturnsAsync(existing);
        mock.Setup(s => s.SaveStudentAsync(It.IsAny<Student>())).Returns(Task.CompletedTask);

        var controller = CreateController(mock);

        // Act
        var result = await controller.UpdateStudent(5, new StudentRequest
        {
            Name = "New Name",
            Email = "new@example.com"
        });

        // Assert
        Assert.IsType<NoContentResult>(result);
        Assert.Equal("New Name", existing.Name);
        Assert.Equal("new@example.com", existing.Email);
        Assert.Null(existing.Phone);
        Assert.Null(existing.Address);
    }

    [Fact]
    public async Task UpdateStudent_UnknownId_Returns404AndDoesNotSave()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentByIdAsync(999)).Returns(Task.FromResult<Student>(null!));

        var controller = CreateController(mock);

        // Act
        var result = await controller.UpdateStudent(999, new StudentRequest
        {
            Name = "Ghost",
            Email = "ghost@example.com"
        });

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
        mock.Verify(s => s.SaveStudentAsync(It.IsAny<Student>()), Times.Never);
    }

    [Fact]
    public async Task PatchStudent_PartialPayload_UpdatesOnlyProvidedFields()
    {
        // Arrange
        var existing = new Student
        {
            Id = 5,
            Name = "Old Name",
            Email = "keep@example.com",
            Phone = "555-0000",
            Address = "Keep this address",
            DateOfEnroll = new DateTime(2023, 1, 15)
        };

        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentByIdAsync(5)).ReturnsAsync(existing);
        mock.Setup(s => s.SaveStudentAsync(It.IsAny<Student>())).Returns(Task.CompletedTask);

        var controller = CreateController(mock);

        // Act
        var result = await controller.PatchStudent(5, new StudentPatchRequest { Name = "New Name" });

        // Assert
        Assert.IsType<NoContentResult>(result);
        Assert.Equal("New Name", existing.Name);
        Assert.Equal("keep@example.com", existing.Email);
        Assert.Equal("555-0000", existing.Phone);
        Assert.Equal("Keep this address", existing.Address);
        Assert.Equal(new DateTime(2023, 1, 15), existing.DateOfEnroll);
    }

    [Fact]
    public async Task PatchStudent_UnknownId_Returns404()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentByIdAsync(999)).Returns(Task.FromResult<Student>(null!));

        var controller = CreateController(mock);

        // Act
        var result = await controller.PatchStudent(999, new StudentPatchRequest { Name = "Ghost" });

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
        mock.Verify(s => s.SaveStudentAsync(It.IsAny<Student>()), Times.Never);
    }

    [Fact]
    public async Task DeleteStudent_ExistingId_Returns204()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentByIdAsync(5)).ReturnsAsync(new Student { Id = 5, Name = "Ada" });
        mock.Setup(s => s.DeleteStudentAsync(5)).Returns(Task.CompletedTask);

        var controller = CreateController(mock);

        // Act
        var result = await controller.DeleteStudent(5);

        // Assert
        Assert.IsType<NoContentResult>(result);
        mock.Verify(s => s.DeleteStudentAsync(5), Times.Once);
    }

    [Fact]
    public async Task DeleteStudent_UnknownId_Returns404AndDoesNotDelete()
    {
        // Arrange
        var mock = CreateServiceMock();
        mock.Setup(s => s.GetStudentByIdAsync(999)).Returns(Task.FromResult<Student>(null!));

        var controller = CreateController(mock);

        // Act
        var result = await controller.DeleteStudent(999);

        // Assert
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
        mock.Verify(s => s.DeleteStudentAsync(It.IsAny<int>()), Times.Never);
    }
}
