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

// On update, ownership must hold for the enrollment's PERSISTED course, not only the
// CourseId in the payload: a teacher could otherwise move an enrollment out of another
// teacher's course into their own (and set its grade) by rewriting CourseId.
public class EnrollmentControllerTests
{
    private const string TeacherName = "teacher-a";
    private const string OtherTeacherName = "teacher-b";

    private static EnrollmentController CreateController(
        Mock<IEnrollmentService> enrollments,
        Mock<ICourseService> courses,
        string name = TeacherName,
        string role = AppRoles.Teacher)
    {
        var controller = new EnrollmentController(
            enrollments.Object,
            new Mock<IStudentService>().Object,
            courses.Object,
            NullLogger<EnrollmentController>.Instance);
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

    private static Mock<ICourseService> CoursesWithOwners()
    {
        var courses = new Mock<ICourseService>();
        courses.Setup(c => c.GetCourseById(1))
            .ReturnsAsync(new Course { Id = 1, Name = "Algebra", Credits = 3, TeacherId = TeacherName });
        courses.Setup(c => c.GetCourseById(2))
            .ReturnsAsync(new Course { Id = 2, Name = "Physics", Credits = 4, TeacherId = OtherTeacherName });
        return courses;
    }

    [Fact]
    public async Task Save_ExistingEnrollmentMovedFromOtherCourse_ReturnsForbid()
    {
        // Arrange — enrollment 10 lives in course 2 (teacher-b); payload retargets course 1 (teacher-a)
        var enrollments = new Mock<IEnrollmentService>();
        enrollments.Setup(e => e.GetEnrollmentById(10))
            .ReturnsAsync(new Enrollment { Id = 10, StudentId = 7, CourseId = 2 });
        var controller = CreateController(enrollments, CoursesWithOwners());

        // Act
        var result = await controller.Save(new Enrollment { Id = 10, StudentId = 7, CourseId = 1, Grade = 90m });

        // Assert
        Assert.IsType<ForbidResult>(result);
        enrollments.Verify(e => e.SaveEnrollment(It.IsAny<Enrollment>()), Times.Never);
    }

    [Fact]
    public async Task Save_ExistingEnrollmentInOwnCourse_ReturnsOk()
    {
        // Arrange
        var enrollments = new Mock<IEnrollmentService>();
        enrollments.Setup(e => e.GetEnrollmentById(10))
            .ReturnsAsync(new Enrollment { Id = 10, StudentId = 7, CourseId = 1 });
        var controller = CreateController(enrollments, CoursesWithOwners());

        // Act
        var result = await controller.Save(new Enrollment { Id = 10, StudentId = 7, CourseId = 1, Grade = 90m });

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(true, ok.Value);
        enrollments.Verify(e => e.SaveEnrollment(It.IsAny<Enrollment>()), Times.Once);
    }

    [Fact]
    public async Task Save_NewEnrollmentInOtherCourse_ReturnsForbid()
    {
        // Arrange
        var enrollments = new Mock<IEnrollmentService>();
        var controller = CreateController(enrollments, CoursesWithOwners());

        // Act
        var result = await controller.Save(new Enrollment { Id = 0, StudentId = 7, CourseId = 2 });

        // Assert
        Assert.IsType<ForbidResult>(result);
        enrollments.Verify(e => e.SaveEnrollment(It.IsAny<Enrollment>()), Times.Never);
    }

    [Fact]
    public async Task Save_NewEnrollmentInOwnCourse_ReturnsOk()
    {
        // Arrange
        var enrollments = new Mock<IEnrollmentService>();
        var controller = CreateController(enrollments, CoursesWithOwners());

        // Act
        var result = await controller.Save(new Enrollment { Id = 0, StudentId = 7, CourseId = 1 });

        // Assert
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(true, ok.Value);
        enrollments.Verify(e => e.SaveEnrollment(It.IsAny<Enrollment>()), Times.Once);
    }

    [Fact]
    public async Task Delete_EnrollmentInOtherCourse_ReturnsFalse()
    {
        // Arrange
        var enrollments = new Mock<IEnrollmentService>();
        enrollments.Setup(e => e.GetEnrollmentById(10))
            .ReturnsAsync(new Enrollment { Id = 10, StudentId = 7, CourseId = 2 });
        var controller = CreateController(enrollments, CoursesWithOwners());

        // Act
        var result = await controller.Delete(10);

        // Assert
        Assert.False(result);
        enrollments.Verify(e => e.DeleteEnrollment(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Delete_EnrollmentInOwnCourse_ReturnsTrue()
    {
        // Arrange
        var enrollments = new Mock<IEnrollmentService>();
        enrollments.Setup(e => e.GetEnrollmentById(10))
            .ReturnsAsync(new Enrollment { Id = 10, StudentId = 7, CourseId = 1 });
        var controller = CreateController(enrollments, CoursesWithOwners());

        // Act
        var result = await controller.Delete(10);

        // Assert
        Assert.True(result);
        enrollments.Verify(e => e.DeleteEnrollment(10), Times.Once);
    }
}
