using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Interfaces;
using StudentManagement.Web.Controllers;
using System.Security.Claims;

namespace StudentManagement.Tests;

public class TeacherControllerTests
{
    private const string TeacherId = "teacher-1";

    private static TeacherController CreateController(Mock<ITeacherService> service, out DefaultHttpContext httpContext)
    {
        httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.Name, TeacherId),
                    new Claim(ClaimTypes.NameIdentifier, TeacherId)
                },
                authenticationType: "Test"))
        };

        var controller = new TeacherController(
            service.Object,
            NullLogger<TeacherController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
        controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        return controller;
    }

    [Fact]
    public async Task UpdateGrade_ServiceReturnsFalse_RedirectsWithTempDataError()
    {
        // Arrange - ownership refusal surfaces from the service as false
        var mockService = new Mock<ITeacherService>();
        mockService.Setup(s => s.UpdateGradeAsync(10, 3.5m, TeacherId)).ReturnsAsync(false);

        var controller = CreateController(mockService, out _);

        // Act
        var result = await controller.UpdateGrade(10, 3.5m);

        // Assert - the signed-in teacher's id is passed for the ownership check,
        // no success is flashed, and an error is posted to TempData
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ManageGrades", redirect.ActionName);
        mockService.Verify(s => s.UpdateGradeAsync(10, 3.5m, TeacherId), Times.Once);
        Assert.Null(controller.TempData["Success"]);
        Assert.NotNull(controller.TempData["Error"]);
    }

    [Fact]
    public async Task UpdateGrade_InvalidModelState_RedirectsWithTempDataError()
    {
        // Arrange - non-numeric form input arrives as a binding error, not a silent null
        var mockService = new Mock<ITeacherService>();
        var controller = CreateController(mockService, out _);
        controller.ModelState.AddModelError("grade", "The value 'abc' is not valid for grade.");

        // Act
        var result = await controller.UpdateGrade(10, null, 7);

        // Assert - redirected back to the same course's roster with an error flashed,
        // and the service is never invoked (the grade must not be cleared)
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ManageGrades", redirect.ActionName);
        Assert.NotNull(redirect.RouteValues);
        Assert.Equal(7, redirect.RouteValues!["courseId"]);
        Assert.NotNull(controller.TempData["Error"]);
        Assert.Null(controller.TempData["Success"]);
        mockService.Verify(
            s => s.UpdateGradeAsync(It.IsAny<int>(), It.IsAny<decimal?>(), It.IsAny<string>()),
            Times.Never);
    }
}
