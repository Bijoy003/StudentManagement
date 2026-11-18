using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Authorization;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Infrastructure.Data;
using StudentManagement.Web.Controllers;

namespace StudentManagement.Tests;

// Ownership on the update path must be checked against the PERSISTED course, never the
// bound payload: a teacher could otherwise spoof TeacherId in the JSON body and edit
// another teacher's course (the service restamps the owner, but the edit still lands).
public class CourseControllerTests
{
    private const string TeacherName = "teacher-a";
    private const string OtherTeacherName = "teacher-b";

    private static Mock<UserManager<ApplicationUser>> CreateUserManager() =>
        new(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    private static CourseController CreateController(
        Mock<ICourseService> service,
        string name = TeacherName,
        string role = AppRoles.Teacher,
        Mock<IEnrollmentService>? enrollmentService = null,
        Mock<UserManager<ApplicationUser>>? userManager = null)
    {
        var controller = new CourseController(
            service.Object,
            (enrollmentService ?? new Mock<IEnrollmentService>()).Object,
            (userManager ?? CreateUserManager()).Object,
            NullLogger<CourseController>.Instance);
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

    [Fact]
    public async Task Save_TeacherCreatesCourseWithCapacity_ReturnsNullCapacityToService()
    {
        // Arrange — capacity is admin-managed; a teacher's payload value must be stripped
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
            MaxEnrollments = 5
        });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(saved);
        Assert.Null(saved!.MaxEnrollments);
    }

    [Fact]
    public async Task Save_TeacherEditsCourseWithSpoofedCapacity_RestampsPersistedCapacity()
    {
        // Arrange — persisted cap is 3; the teacher's payload claims 99
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetCourseById(5))
            .ReturnsAsync(new Course { Id = 5, Name = "Algebra", Credits = 3, TeacherId = TeacherName, MaxEnrollments = 3 });
        Course? saved = null;
        service.Setup(s => s.SaveCourse(It.IsAny<Course>())).Callback<Course>(c => saved = c);
        var controller = CreateController(service);

        // Act
        var result = await controller.Save(new Course
        {
            Id = 5,
            Name = "Algebra II",
            Credits = 4,
            MaxEnrollments = 99
        });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(saved);
        Assert.Equal(3, saved!.MaxEnrollments);
    }

    [Fact]
    public async Task Save_AdminSetsCapacity_ReturnsBoundCapacityKept()
    {
        // Arrange — regression guard: admins keep full control of the cap
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
            MaxEnrollments = 5
        });

        // Assert
        Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(saved);
        Assert.Equal(5, saved!.MaxEnrollments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxEnrollments_BelowOne_ReturnsValidationError(int capacity)
    {
        // Arrange — the cap must be at least 1 when set (null = unlimited stays valid)
        var course = new Course { Name = "Algebra", Credits = 3, MaxEnrollments = capacity };
        var results = new List<ValidationResult>();

        // Act
        var valid = Validator.TryValidateObject(course, new ValidationContext(course), results, true);

        // Assert
        Assert.False(valid);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(Course.MaxEnrollments)));
    }

    [Fact]
    public async Task Index_LinkedStudent_ReturnsOwnEnrolledAndFullCourseIds()
    {
        // Arrange — student 7 is enrolled in courses 1 and 2; course 1 (cap 2) and
        // course 3 (cap 1) are full, course 2 is unlimited
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetAllCourses()).ReturnsAsync(new List<Course>
        {
            new() { Id = 1, Name = "Algebra", Credits = 3, MaxEnrollments = 2 },
            new() { Id = 2, Name = "Physics", Credits = 4 },
            new() { Id = 3, Name = "Chemistry", Credits = 3, MaxEnrollments = 1 }
        });
        var enrollmentService = new Mock<IEnrollmentService>();
        enrollmentService.Setup(s => s.GetEnrollments()).ReturnsAsync(new List<Enrollment>
        {
            new() { Id = 1, StudentId = 7, CourseId = 1 },
            new() { Id = 2, StudentId = 8, CourseId = 1 },
            new() { Id = 3, StudentId = 7, CourseId = 2 },
            new() { Id = 4, StudentId = 8, CourseId = 3 }
        });
        var userManager = CreateUserManager();
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Linked Student", StudentId = 7 });
        var controller = CreateController(service, "student@example.com", AppRoles.Student, enrollmentService, userManager);

        // Act
        var result = await controller.Index();

        // Assert
        Assert.IsType<ViewResult>(result);
        var enrolled = Assert.IsType<HashSet<int>>((object)controller.ViewBag.EnrolledCourseIds);
        Assert.Equal(new[] { 1, 2 }, enrolled.OrderBy(id => id));
        var full = Assert.IsType<HashSet<int>>((object)controller.ViewBag.FullCourseIds);
        Assert.Equal(new[] { 1, 3 }, full.OrderBy(id => id));
    }

    [Fact]
    public async Task Index_UnlinkedStudent_ReturnsEmptyEnrolledAndFullCourseIds()
    {
        // Arrange
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetAllCourses()).ReturnsAsync(new List<Course>
        {
            new() { Id = 1, Name = "Algebra", Credits = 3, MaxEnrollments = 1 }
        });
        var userManager = CreateUserManager();
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync(new ApplicationUser { FullName = "Unlinked Student" });
        var controller = CreateController(
            service, "student@example.com", AppRoles.Student, new Mock<IEnrollmentService>(), userManager);

        // Act
        var result = await controller.Index();

        // Assert
        Assert.IsType<ViewResult>(result);
        var enrolled = Assert.IsType<HashSet<int>>((object)controller.ViewBag.EnrolledCourseIds);
        Assert.Empty(enrolled);
        var full = Assert.IsType<HashSet<int>>((object)controller.ViewBag.FullCourseIds);
        Assert.Empty(full);
    }

    [Fact]
    public async Task Index_StaffRole_ReturnsCatalogWithoutEnrollmentQueries()
    {
        // Arrange — regression guard: the staff catalog must not pay for student data
        var service = new Mock<ICourseService>();
        service.Setup(s => s.GetAllCourses()).ReturnsAsync(new List<Course>
        {
            new() { Id = 1, Name = "Algebra", Credits = 3 }
        });
        var enrollmentService = new Mock<IEnrollmentService>();
        var controller = CreateController(service, enrollmentService: enrollmentService);

        // Act
        var result = await controller.Index();

        // Assert
        Assert.IsType<ViewResult>(result);
        enrollmentService.Verify(s => s.GetEnrollments(), Times.Never);
    }
}
