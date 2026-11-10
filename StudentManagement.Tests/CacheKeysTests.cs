using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application;
using StudentManagement.Application.Services;
using StudentManagement.Domain.DTOs;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

// WP8 AC8 — CacheKeys constants class in Application; Course/Enrollment/Teacher/Student
// services must reference the constants (observable: the exact keys they read/write/evict
// equal the CacheKeys values). Literal-duplication guard: every assertion goes through
// CacheKeys, so a divergent constant or a leftover hard-coded literal fails the match.
public class CacheKeysTests
{
    private static Mock<IDistributedCache> CreateCacheMock()
    {
        // NOTE: GetStringAsync/SetStringAsync are extension methods over the string-based
        // interface members — Moq must intercept GetAsync/SetAsync (what the extensions call).
        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync((byte[]?)null);
        cache.Setup(c => c.SetAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        return cache;
    }

    private static readonly List<CourseStudentCountDto> EmptyCounts = new();

    // ---------- the constants themselves ----------

    [Fact]
    public void CacheKeys_WellKnownKeys_ReturnsExistingLiteralValues()
    {
        // Arrange / Act / Assert — values must stay byte-identical to today's literals
        Assert.Equal("courses_all", CacheKeys.AllCourses);
        Assert.Equal("students_all", CacheKeys.AllStudents);
        Assert.Equal("enrollments_all", CacheKeys.AllEnrollments);
        Assert.Equal("course_student_counts", CacheKeys.CourseStudentCounts);
    }

    [Fact]
    public void CacheKeys_KeyedHelpers_ReturnsExistingPrefixedKeys()
    {
        // Arrange / Act / Assert — same shapes the services build inline today
        Assert.Equal("course_7", CacheKeys.Course(7));
        Assert.Equal("student_3", CacheKeys.Student(3));
        Assert.Equal("enrollment_9", CacheKeys.Enrollment(9));
        Assert.Equal("enrollment_course_2", CacheKeys.EnrollmentsByCourse(2));
        Assert.Equal("enrollment_students_course_2", CacheKeys.StudentsInCourse(2));
        Assert.Equal("enrollment_courses_student_4", CacheKeys.CoursesForStudent(4));
    }

    // ---------- observable: services use the constant keys ----------

    [Fact]
    public async Task CourseService_GetStudentCountPerCourse_CacheMiss_ReturnsCachedUnderCourseCountsConstant()
    {
        // Arrange
        var cache = CreateCacheMock();
        var repository = new Mock<ICourseRepository>();
        repository.Setup(r => r.GetStudentCountPerCourseAsyncReadOnly()).ReturnsAsync(EmptyCounts);
        var service = new CourseService(repository.Object, cache.Object, NullLogger<CourseService>.Instance);

        // Act
        await service.GetStudentCountPerCourse();

        // Assert — read AND write must hit the CacheKeys constant, nothing else
        cache.Verify(c => c.GetAsync(CacheKeys.CourseStudentCounts, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(
            c => c.SetAsync(CacheKeys.CourseStudentCounts, It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CourseService_SaveCourse_ExistingCourse_ReturnsEvictsCourseCountKeysFromConstants()
    {
        // Arrange
        var cache = CreateCacheMock();
        var repository = new Mock<ICourseRepository>();
        repository.Setup(r => r.GetByIdAsyncReadOnly(5))
                  .ReturnsAsync(new Course { Id = 5, Name = "Algebra", Credits = 3 });
        repository.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        var service = new CourseService(repository.Object, cache.Object, NullLogger<CourseService>.Instance);

        // Act
        await service.SaveCourse(new Course { Id = 5, Name = "Algebra II", Credits = 3 });

        // Assert
        cache.Verify(c => c.RemoveAsync(CacheKeys.AllCourses, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKeys.CourseStudentCounts, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKeys.Course(5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnrollmentService_SaveEnrollment_NewEnrollment_ReturnsEvictsCourseCountsKeyFromConstant()
    {
        // Arrange
        var cache = CreateCacheMock();
        var repository = new Mock<IEnrollmentRepository>();
        repository.Setup(r => r.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
        repository.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        var service = new EnrollmentService(
            repository.Object,
            new MemoryCache(new MemoryCacheOptions()),
            cache.Object,
            NullLogger<EnrollmentService>.Instance);

        // Act
        await service.SaveEnrollment(new Enrollment { Id = 0, CourseId = 1, StudentId = 2 });

        // Assert — same eviction the existing hard-coded test pins; now via the constant
        cache.Verify(c => c.RemoveAsync(CacheKeys.CourseStudentCounts, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TeacherService_SaveCourseAsync_OwnedCourse_ReturnsEvictsCourseKeysFromConstants()
    {
        // Arrange
        var cache = CreateCacheMock();
        var courseRepository = new Mock<ICourseRepository>();
        courseRepository.Setup(r => r.GetByIdAsyncReadOnly(6))
                        .ReturnsAsync(new Course { Id = 6, Name = "Calc", Credits = 4, TeacherId = "t1" });
        courseRepository.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        var service = new TeacherService(
            courseRepository.Object,
            new Mock<IEnrollmentRepository>().Object,
            cache.Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<TeacherService>.Instance);

        // Act
        var result = await service.SaveCourseAsync(new Course { Id = 6, Name = "Calc", Credits = 4 }, "t1");

        // Assert
        Assert.True(result);
        cache.Verify(c => c.RemoveAsync(CacheKeys.AllCourses, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKeys.CourseStudentCounts, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKeys.Course(6), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StudentService_SaveStudentAsync_ExistingStudent_ReturnsEvictsStudentKeysFromConstants()
    {
        // Arrange
        var cache = CreateCacheMock();
        var repository = new Mock<IStudentRepository>();
        repository.Setup(r => r.SaveAsync()).Returns(Task.CompletedTask);
        var service = new StudentService(
            repository.Object,
            new Mock<IEnrollmentRepository>().Object,
            cache.Object,
            NullLogger<StudentService>.Instance);

        // Act
        await service.SaveStudentAsync(new Student { Id = 8, Name = "Ada", Email = "ada@example.com" });

        // Assert
        cache.Verify(c => c.RemoveAsync(CacheKeys.AllStudents, It.IsAny<CancellationToken>()), Times.Once);
        cache.Verify(c => c.RemoveAsync(CacheKeys.Student(8), It.IsAny<CancellationToken>()), Times.Once);
    }
}
