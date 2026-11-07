using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

public class TeacherServiceTests
{
    private const string TeacherId = "teacher-1";
    private const string OtherTeacherId = "teacher-2";

    private static TeacherService CreateService(
        Mock<ICourseRepository> courseRepo,
        Mock<IEnrollmentRepository> enrollmentRepo)
    {
        // Loose cache/logger dependencies: TeacherService invalidates CourseService/
        // EnrollmentService cache keys on successful writes; Remove calls must not throw.
        return new TeacherService(
            courseRepo.Object,
            enrollmentRepo.Object,
            new Mock<IDistributedCache>().Object,
            new MemoryCache(new MemoryCacheOptions()),
            new Mock<ILogger<TeacherService>>().Object);
    }

    private static Course OwnedCourse(int id = 5) =>
        new() { Id = id, Name = "Algebra", Credits = 3, TeacherId = TeacherId };

    private static Course ForeignCourse(int id = 5) =>
        new() { Id = id, Name = "Physics", Credits = 4, TeacherId = OtherTeacherId };

    // Sets up every plausible ownership-lookup path so the tests hold whichever one the
    // implementation picks (write context, read-only context, or teacher-scoped list).
    private static void SetupCourseLookups(Mock<ICourseRepository> courseRepo, int courseId, Course? course)
    {
        courseRepo.Setup(r => r.GetByIdAsync(courseId)).ReturnsAsync(course);
        courseRepo.Setup(r => r.GetByIdAsyncReadOnly(courseId)).ReturnsAsync(course);
        courseRepo.Setup(r => r.GetByTeacherIdAsyncReadOnly(TeacherId))
                  .ReturnsAsync(course != null ? new List<Course> { course } : new List<Course>());
    }

    [Fact]
    public async Task GetTeacherCoursesAsync_UsesTeacherScopedReadOnlyQuery_ReturnsCourses()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        var expected = new List<Course>
        {
            new() { Id = 1, Name = "Algebra", Credits = 3, TeacherId = TeacherId },
            new() { Id = 2, Name = "Geometry", Credits = 4, TeacherId = TeacherId }
        };
        mockCourseRepo.Setup(r => r.GetByTeacherIdAsyncReadOnly(TeacherId)).ReturnsAsync(expected);

        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.GetTeacherCoursesAsync(TeacherId);

        // Assert - the teacher-scoped read-only query is used; no GetAll + in-memory filtering
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { 1, 2 }, result.Select(c => c.Id));
        mockCourseRepo.Verify(r => r.GetByTeacherIdAsyncReadOnly(TeacherId), Times.Once);
        mockCourseRepo.Verify(r => r.GetAllAsync(), Times.Never);
        mockCourseRepo.Verify(r => r.GetAllAsyncReadOnly(), Times.Never);
    }

    [Fact]
    public async Task SaveCourseAsync_NewCourse_ReturnsTrue()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        var course = new Course { Id = 0, Name = "Algebra", Credits = 3, TeacherId = TeacherId };

        // Act
        var result = await service.SaveCourseAsync(course, TeacherId);

        // Assert
        Assert.True(result);
        mockCourseRepo.Verify(r => r.AddAsync(course), Times.Once);
        mockCourseRepo.Verify(r => r.Update(It.IsAny<Course>()), Times.Never);
        mockCourseRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task SaveCourseAsync_NewCourse_InvalidatesCourseCaches()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        var mockCache = new Mock<IDistributedCache>();
        var service = new TeacherService(
            mockCourseRepo.Object,
            mockEnrollmentRepo.Object,
            mockCache.Object,
            new MemoryCache(new MemoryCacheOptions()),
            new Mock<ILogger<TeacherService>>().Object);

        var course = new Course { Id = 0, Name = "Algebra", Credits = 3, TeacherId = TeacherId };

        // Act
        var result = await service.SaveCourseAsync(course, TeacherId);

        // Assert - the CourseService-owned distributed keys are evicted so the new course
        // shows up immediately in cached lists (e.g. the /Enrollment/Create dropdown)
        Assert.True(result);
        mockCache.Verify(c => c.RemoveAsync("courses_all", It.IsAny<CancellationToken>()), Times.Once);
        mockCache.Verify(c => c.RemoveAsync("course_student_counts", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveCourseAsync_OwnedExistingCourse_ReturnsTrue()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 5, OwnedCourse(5));
        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        var submitted = new Course { Id = 5, Name = "Algebra II", Credits = 4, TeacherId = TeacherId };

        // Act
        var result = await service.SaveCourseAsync(submitted, TeacherId);

        // Assert
        Assert.True(result);
        mockCourseRepo.Verify(r => r.Update(It.IsAny<Course>()), Times.Once);
        mockCourseRepo.Verify(r => r.AddAsync(It.IsAny<Course>()), Times.Never);
        mockCourseRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task SaveCourseAsync_CourseOwnedByAnotherTeacher_ReturnsFalse()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 5, ForeignCourse(5));
        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        var submitted = new Course { Id = 5, Name = "Hijacked", Credits = 3, TeacherId = TeacherId };

        // Act
        var result = await service.SaveCourseAsync(submitted, TeacherId);

        // Assert - ownership refusal: nothing is written or persisted
        Assert.False(result);
        mockCourseRepo.Verify(r => r.Update(It.IsAny<Course>()), Times.Never);
        mockCourseRepo.Verify(r => r.AddAsync(It.IsAny<Course>()), Times.Never);
        mockCourseRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task SaveCourseAsync_ExistingCourseNotFound_ReturnsFalse()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 99, null);
        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        var submitted = new Course { Id = 99, Name = "Ghost", Credits = 3, TeacherId = TeacherId };

        // Act
        var result = await service.SaveCourseAsync(submitted, TeacherId);

        // Assert
        Assert.False(result);
        mockCourseRepo.Verify(r => r.Update(It.IsAny<Course>()), Times.Never);
        mockCourseRepo.Verify(r => r.AddAsync(It.IsAny<Course>()), Times.Never);
        mockCourseRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteCourseAsync_OwnedCourse_ReturnsTrue()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 5, OwnedCourse(5));
        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.DeleteCourseAsync(5, TeacherId);

        // Assert
        Assert.True(result);
        mockCourseRepo.Verify(r => r.DeleteAsync(5), Times.Once);
        mockCourseRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task DeleteCourseAsync_CourseOwnedByAnotherTeacher_ReturnsFalse()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 5, ForeignCourse(5));
        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.DeleteCourseAsync(5, TeacherId);

        // Assert - IDOR refusal: nothing is deleted or persisted
        Assert.False(result);
        mockCourseRepo.Verify(r => r.DeleteAsync(It.IsAny<int>()), Times.Never);
        mockCourseRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DeleteCourseAsync_CourseNotFound_ReturnsFalse()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 99, null);
        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.DeleteCourseAsync(99, TeacherId);

        // Assert
        Assert.False(result);
        mockCourseRepo.Verify(r => r.DeleteAsync(It.IsAny<int>()), Times.Never);
        mockCourseRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task GetEnrollmentsByCourseAsync_UsesReadOnlyWithStudentInclude_ReturnsEnrollments()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        var expected = new List<Enrollment>
        {
            new() { Id = 1, CourseId = 7, StudentId = 11, Student = new Student { Id = 11, Name = "Ada Lovelace" } },
            new() { Id = 2, CourseId = 7, StudentId = 12, Student = new Student { Id = 12, Name = "Grace Hopper" } }
        };
        mockEnrollmentRepo.Setup(r => r.GetByCourseIdWithStudentAsyncReadOnly(7)).ReturnsAsync(expected);

        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.GetEnrollmentsByCourseAsync(7);

        // Assert - the Student-including read-only query is used, not the tracked/plain variants
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.NotNull(e.Student));
        mockEnrollmentRepo.Verify(r => r.GetByCourseIdWithStudentAsyncReadOnly(7), Times.Once);
        mockEnrollmentRepo.Verify(r => r.GetByCourseIdAsync(It.IsAny<int>()), Times.Never);
        mockEnrollmentRepo.Verify(r => r.GetByCourseIdAsyncReadOnly(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task UpdateGradeAsync_OwnedCourseEnrollment_ReturnsTrue()
    {
        // Arrange
        var course = OwnedCourse(5);
        var enrollment = new Enrollment { Id = 10, CourseId = 5, StudentId = 11, Grade = null, Course = course };

        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 5, course);
        mockEnrollmentRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(enrollment);

        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.UpdateGradeAsync(10, 3.75m, TeacherId);

        // Assert
        Assert.True(result);
        Assert.Equal(3.75m, enrollment.Grade);
        mockEnrollmentRepo.Verify(r => r.Update(It.IsAny<Enrollment>()), Times.Once);
        mockEnrollmentRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateGradeAsync_NullGrade_ReturnsTrue()
    {
        // Arrange - clearing an existing grade is a legal write for the owning teacher
        var course = OwnedCourse(5);
        var enrollment = new Enrollment { Id = 10, CourseId = 5, StudentId = 11, Grade = 2.5m, Course = course };

        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 5, course);
        mockEnrollmentRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(enrollment);

        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.UpdateGradeAsync(10, null, TeacherId);

        // Assert
        Assert.True(result);
        Assert.Null(enrollment.Grade);
        mockEnrollmentRepo.Verify(r => r.Update(It.IsAny<Enrollment>()), Times.Once);
        mockEnrollmentRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task UpdateGradeAsync_GradeBelowZero_ReturnsFalse()
    {
        // Arrange - an owned enrollment that would otherwise accept the write
        var course = OwnedCourse(5);
        var enrollment = new Enrollment { Id = 10, CourseId = 5, StudentId = 11, Grade = 3.0m, Course = course };

        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 5, course);
        mockEnrollmentRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(enrollment);

        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.UpdateGradeAsync(10, -1m, TeacherId);

        // Assert - out-of-range grade is refused: untouched, nothing written or persisted
        Assert.False(result);
        Assert.Equal(3.0m, enrollment.Grade);
        mockEnrollmentRepo.Verify(r => r.Update(It.IsAny<Enrollment>()), Times.Never);
        mockEnrollmentRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateGradeAsync_GradeAbove100_ReturnsFalse()
    {
        // Arrange - an owned enrollment that would otherwise accept the write
        var course = OwnedCourse(5);
        var enrollment = new Enrollment { Id = 10, CourseId = 5, StudentId = 11, Grade = 3.0m, Course = course };

        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 5, course);
        mockEnrollmentRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(enrollment);

        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.UpdateGradeAsync(10, 101m, TeacherId);

        // Assert - out-of-range grade is refused: untouched, nothing written or persisted
        Assert.False(result);
        Assert.Equal(3.0m, enrollment.Grade);
        mockEnrollmentRepo.Verify(r => r.Update(It.IsAny<Enrollment>()), Times.Never);
        mockEnrollmentRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateGradeAsync_EnrollmentInAnotherTeachersCourse_ReturnsFalse()
    {
        // Arrange
        var course = ForeignCourse(5);
        var enrollment = new Enrollment { Id = 10, CourseId = 5, StudentId = 11, Grade = 1.0m, Course = course };

        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        SetupCourseLookups(mockCourseRepo, 5, course);
        mockEnrollmentRepo.Setup(r => r.GetByIdAsync(10)).ReturnsAsync(enrollment);

        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.UpdateGradeAsync(10, 4.0m, TeacherId);

        // Assert - IDOR refusal: grade untouched, nothing written or persisted
        Assert.False(result);
        Assert.Equal(1.0m, enrollment.Grade);
        mockEnrollmentRepo.Verify(r => r.Update(It.IsAny<Enrollment>()), Times.Never);
        mockEnrollmentRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task UpdateGradeAsync_EnrollmentNotFound_ReturnsFalse()
    {
        // Arrange
        var mockCourseRepo = new Mock<ICourseRepository>();
        var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
        mockEnrollmentRepo.Setup(r => r.GetByIdAsync(999)).ReturnsAsync((Enrollment?)null);

        var service = CreateService(mockCourseRepo, mockEnrollmentRepo);

        // Act
        var result = await service.UpdateGradeAsync(999, 4.0m, TeacherId);

        // Assert
        Assert.False(result);
        mockEnrollmentRepo.Verify(r => r.Update(It.IsAny<Enrollment>()), Times.Never);
        mockEnrollmentRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }
}
