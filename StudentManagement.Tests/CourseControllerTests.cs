using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Web.Controllers;

namespace StudentManagement.Tests;

// Ownership on the update path must be checked against the PERSISTED course, never the
// bound payload: a teacher could otherwise spoof TeacherId in the JSON body and edit
// another teacher's course (the service restamps the owner, but the edit still lands).
public class CourseControllerTests
{
    private const string TeacherName = "teacher-a";
    private const string OtherTeacherName = "teacher-b";

    private static CourseController CreateController(
        Mock<ICourseService> service,
        string name = TeacherName,
        string role = AppRoles.Teacher)
    {
        var controller = new CourseController(service.Object, NullLogger<CourseController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, name),
                    new Claim(ClaimTypes.Role, role)
                ], "TestAuth"))
            }
        };
        return controller;
    }

    [Fact]
    public async Task Save_ExistingCourseWithSpoofedOwner_ReturnsForbid()
    {
        // Arrange — persisted owner is teacher-b; the payload claims teacher-a
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetCourseById(5))
            .ReturnsAsync(new Course { Id = 5, Name = "Algebra", Credits = 3, TeacherId = OtherTeacherName });
        var controller = CreateController(service);

        // Act
        var result = await controller.Save(new Course
        {
            Id = 5,
            Name = "Hijacked",
            Credits = 3,
            TeacherId = TeacherName
        });

        // Assert
        Assert.IsType<ForbidResult>(result);
        service.Verify(s => s.SaveCourse(It.IsAny<Course>()), Times.Never);
    }

    [Fact]
    public async Task Save_ExistingOwnedCourse_ReturnsOk()
    {
        // Arrange
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetCourseById(5))
            .ReturnsAsync(new Course { Id = 5, Name = "Algebra", Credits = 3, TeacherId = TeacherName });
        var controller = CreateController(service);

        // Act
        var result = await controller.Save(new Course { Id = 5, Name = "Algebra II", Credits = 4 });

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(true, ok.Value);
        service.Verify(s => s.SaveCourse(It.IsAny<Course>()), Times.Once);
    }

    [Fact]
    public async Task Save_ExistingCourseAsAdmin_ReturnsOk()
    {
        // Arrange — admins manage every course regardless of owner
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetCourseById(5))
            .ReturnsAsync(new Course { Id = 5, Name = "Algebra", Credits = 3, TeacherId = OtherTeacherName });
        var controller = CreateController(service, "admin@admin.com", AppRoles.Admin);

        // Act
        var result = await controller.Save(new Course { Id = 5, Name = "Algebra II", Credits = 4 });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        service.Verify(s => s.SaveCourse(It.IsAny<Course>()), Times.Once);
    }

    [Fact]
    public async Task Save_NewCourseAsTeacher_ReturnsStampedOwner()
    {
        // Arrange — a crafted TeacherId in the payload must be overwritten server-side
        var service = new Mock<ICourseService>();
        Course? saved = null;
        service.Setup(s => s.SaveCourse(It.IsAny<Course>())).Callback<Course>(c => saved = c);
        var controller = CreateController(service);

        // Act
        var result = await controller.Save(new Course
        {
            Id = 0,
            Name = "New Course",
            Credits = 3,
            TeacherId = OtherTeacherName
        });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(saved);
        Assert.Equal(TeacherName, saved!.TeacherId);
    }

    [Fact]
    public async Task Save_NewCourseAsAdmin_ReturnsBoundOwnerKept()
    {
        // Arrange — admins may assign ownership explicitly on create
        var service = new Mock<ICourseService>();
        Course? saved = null;
        service.Setup(s => s.SaveCourse(It.IsAny<Course>())).Callback<Course>(c => saved = c);
        var controller = CreateController(service, "admin@admin.com", AppRoles.Admin);

        // Act
        var result = await controller.Save(new Course
        {
            Id = 0,
            Name = "New Course",
            Credits = 3,
            TeacherId = OtherTeacherName
        });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(saved);
        Assert.Equal(OtherTeacherName, saved!.TeacherId);
    }

    [Fact]
    public async Task Delete_CourseOwnedByOtherTeacher_ReturnsFalse()
    {
        // Arrange
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetCourseById(5))
            .ReturnsAsync(new Course { Id = 5, Name = "Algebra", Credits = 3, TeacherId = OtherTeacherName });
        var controller = CreateController(service);

        // Act
        var result = await controller.Delete(5);

        // Assert
        Assert.False(result);
        service.Verify(s => s.DeleteCourse(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Delete_OwnedCourse_ReturnsTrue()
    {
        // Arrange
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetCourseById(5))
            .ReturnsAsync(new Course { Id = 5, Name = "Algebra", Credits = 3, TeacherId = TeacherName });
        var controller = CreateController(service);

        // Act
        var result = await controller.Delete(5);

        // Assert
        Assert.True(result);
        service.Verify(s => s.DeleteCourse(5), Times.Once);
    }
}
