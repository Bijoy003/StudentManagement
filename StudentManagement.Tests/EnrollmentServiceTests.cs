using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

public class EnrollmentServiceTests
{
    [Fact]
    public async Task GetEnrollments_CalledTwice_QueriesRepositoryOnce()
    {
        // Arrange
        var enrollments = new List<Enrollment>
        {
            new() { Id = 1, CourseId = 1, StudentId = 2 },
            new() { Id = 2, CourseId = 1, StudentId = 3 }
        };

        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetAllAsyncReadOnly()).ReturnsAsync(enrollments);

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        // Act
        var first = await service.GetEnrollments();
        var second = await service.GetEnrollments();

        // Assert
        Assert.Equal(2, first.Count());
        Assert.Equal(2, second.Count());
        mockRepo.Verify(r => r.GetAllAsyncReadOnly(), Times.Once);
    }

    [Fact]
    public async Task GetEnrollments_CalledTwice_ReturnsEqualSnapshot()
    {
        // Arrange - a new list per repository call, so a single repository call can only
        // be followed by equal content if the in-process cache served the second read.
        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetAllAsyncReadOnly())
                .ReturnsAsync(() => new List<Enrollment>
                {
                    new() { Id = 1, CourseId = 1, StudentId = 2, Grade = 3m }
                });

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        // Act
        var first = await service.GetEnrollments();
        var second = await service.GetEnrollments();

        // Assert - the cache stores a serialized snapshot, so each caller gets its own copy
        Assert.NotSame(first, second);
        var firstEnrollment = Assert.Single(first);
        var secondEnrollment = Assert.Single(second);
        Assert.NotSame(firstEnrollment, secondEnrollment);
        Assert.Equal(firstEnrollment.Id, secondEnrollment.Id);
        Assert.Equal(firstEnrollment.CourseId, secondEnrollment.CourseId);
        Assert.Equal(firstEnrollment.StudentId, secondEnrollment.StudentId);
        Assert.Equal(firstEnrollment.Grade, secondEnrollment.Grade);
        mockRepo.Verify(r => r.GetAllAsyncReadOnly(), Times.Once);
    }

    [Fact]
    public async Task GetEnrollmentById_WhenCached_QueriesRepositoryOnce()
    {
        // Arrange
        var enrollment = new Enrollment { Id = 1, CourseId = 4, StudentId = 7, Grade = 3.5m };

        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetByIdAsyncReadOnly(1)).ReturnsAsync(enrollment);

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        // Act
        var first = await service.GetEnrollmentById(1);
        var second = await service.GetEnrollmentById(1);

        // Assert - the cached entry is a serialized snapshot, not the tracked instance
        Assert.Equal(1, first!.Id);
        Assert.Equal(1, second!.Id);
        Assert.NotSame(first, second);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Grade, second.Grade);
        mockRepo.Verify(r => r.GetByIdAsyncReadOnly(1), Times.Once);
    }

    [Fact]
    public async Task GetEnrollmentById_WhenMissing_DoesNotCacheNull()
    {
        // Arrange
        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetByIdAsyncReadOnly(99)).ReturnsAsync((Enrollment)null!);

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        // Act
        var first = await service.GetEnrollmentById(99);
        var second = await service.GetEnrollmentById(99);

        // Assert
        Assert.Null(first);
        Assert.Null(second);
        mockRepo.Verify(r => r.GetByIdAsyncReadOnly(99), Times.Exactly(2));
    }

    [Fact]
    public async Task SaveEnrollment_AfterListCached_InvalidatesListCache()
    {
        // Arrange
        var enrollments = new List<Enrollment>
        {
            new() { Id = 1, CourseId = 1, StudentId = 2 }
        };

        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetAllAsyncReadOnly()).ReturnsAsync(enrollments);
        mockRepo.Setup(r => r.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
        mockRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        await service.GetEnrollments();

        // Act
        await service.SaveEnrollment(new Enrollment { Id = 0, CourseId = 1, StudentId = 2 });
        await service.GetEnrollments();

        // Assert
        mockRepo.Verify(r => r.GetAllAsyncReadOnly(), Times.Exactly(2));
        mockRepo.Verify(r => r.AddAsync(It.IsAny<Enrollment>()), Times.Once);
    }

    [Fact]
    public async Task SaveEnrollment_AfterDependentListsCached_InvalidatesCourseAndStudentKeys()
    {
        // Arrange
        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetStudentsByCourseAsyncReadOnly(1))
                .ReturnsAsync(new List<Student> { new() { Id = 2, Name = "Jane Doe" } });
        mockRepo.Setup(r => r.GetCoursesByStudentAsyncReadOnly(2))
                .ReturnsAsync(new List<Course> { new() { Id = 1, Name = "Math", Credits = 3 } });
        mockRepo.Setup(r => r.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
        mockRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        await service.GetStudentsInCourse(1);
        await service.GetCoursesForStudent(2);

        // Act
        await service.SaveEnrollment(new Enrollment { Id = 0, CourseId = 1, StudentId = 2 });
        await service.GetStudentsInCourse(1);
        await service.GetCoursesForStudent(2);

        // Assert
        mockRepo.Verify(r => r.GetStudentsByCourseAsyncReadOnly(1), Times.Exactly(2));
        mockRepo.Verify(r => r.GetCoursesByStudentAsyncReadOnly(2), Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteEnrollment_AfterCachesPopulated_InvalidatesAffectedKeys()
    {
        // Arrange
        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(5))
                .ReturnsAsync(new Enrollment { Id = 5, CourseId = 1, StudentId = 2 });
        mockRepo.Setup(r => r.GetAllAsyncReadOnly())
                .ReturnsAsync(new List<Enrollment> { new() { Id = 5, CourseId = 1, StudentId = 2 } });
        mockRepo.Setup(r => r.GetStudentsByCourseAsyncReadOnly(1))
                .ReturnsAsync(new List<Student> { new() { Id = 2, Name = "Jane Doe" } });
        mockRepo.Setup(r => r.GetCoursesByStudentAsyncReadOnly(2))
                .ReturnsAsync(new List<Course> { new() { Id = 1, Name = "Math", Credits = 3 } });
        mockRepo.Setup(r => r.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        mockRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        await service.GetEnrollments();
        await service.GetStudentsInCourse(1);
        await service.GetCoursesForStudent(2);

        // Act
        await service.DeleteEnrollment(5);
        await service.GetEnrollments();
        await service.GetStudentsInCourse(1);
        await service.GetCoursesForStudent(2);

        // Assert
        mockRepo.Verify(r => r.GetAllAsyncReadOnly(), Times.Exactly(2));
        mockRepo.Verify(r => r.GetStudentsByCourseAsyncReadOnly(1), Times.Exactly(2));
        mockRepo.Verify(r => r.GetCoursesByStudentAsyncReadOnly(2), Times.Exactly(2));
        mockRepo.Verify(r => r.GetByIdAsync(5), Times.Once);
        mockRepo.Verify(r => r.DeleteAsync(5), Times.Once);
    }

    [Fact]
    public async Task SaveEnrollment_Always_InvalidatesCourseStudentCountsKey()
    {
        // Arrange
        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
        mockRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var mockDistributedCache = CreateDistributedCacheMock();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, mockDistributedCache);

        // Act
        await service.SaveEnrollment(new Enrollment { Id = 0, CourseId = 1, StudentId = 2 });

        // Assert
        mockDistributedCache.Verify(
            c => c.RemoveAsync("course_student_counts", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEnrollments_AfterSave_ReturnsFreshDataFromRepository()
    {
        // Arrange
        var staleList = new List<Enrollment>
        {
            new() { Id = 1, CourseId = 1, StudentId = 2 }
        };
        var freshList = new List<Enrollment>
        {
            new() { Id = 1, CourseId = 1, StudentId = 2 },
            new() { Id = 2, CourseId = 1, StudentId = 3 }
        };

        var repositoryCalls = 0;
        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetAllAsyncReadOnly())
                .ReturnsAsync(() => ++repositoryCalls == 1
                    ? (IEnumerable<Enrollment>)staleList
                    : (IEnumerable<Enrollment>)freshList);
        mockRepo.Setup(r => r.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
        mockRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        var beforeSave = await service.GetEnrollments();

        // Act
        await service.SaveEnrollment(new Enrollment { Id = 0, CourseId = 1, StudentId = 3 });
        var afterSave = await service.GetEnrollments();

        // Assert
        Assert.Single(beforeSave);
        Assert.Equal(2, afterSave.Count());
        Assert.Equal(2, repositoryCalls);
    }

    [Fact]
    public async Task SaveEnrollment_WhenUpdatingExisting_InvalidatesEntryById()
    {
        // Arrange
        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetByIdAsyncReadOnly(5))
                .ReturnsAsync(new Enrollment { Id = 5, CourseId = 1, StudentId = 2, Grade = 3m });
        mockRepo.Setup(r => r.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
        mockRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        await service.GetEnrollmentById(5);

        // Act
        await service.SaveEnrollment(new Enrollment { Id = 5, CourseId = 1, StudentId = 2, Grade = 3m });
        await service.GetEnrollmentById(5);

        // Assert
        mockRepo.Verify(r => r.Update(It.IsAny<Enrollment>()), Times.Once);
        mockRepo.Verify(r => r.AddAsync(It.IsAny<Enrollment>()), Times.Never);
        mockRepo.Verify(r => r.GetByIdAsyncReadOnly(5), Times.Exactly(2));
    }

    [Fact]
    public async Task DeleteEnrollment_WhenRowMissing_InvalidatesListAndCounts()
    {
        // Arrange
        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((Enrollment)null!);
        mockRepo.Setup(r => r.GetAllAsyncReadOnly())
                .ReturnsAsync(() => new List<Enrollment> { new() { Id = 1, CourseId = 1, StudentId = 2 } });
        mockRepo.Setup(r => r.DeleteAsync(It.IsAny<int>())).Returns(Task.CompletedTask);
        mockRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        var mockDistributedCache = CreateDistributedCacheMock();
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, mockDistributedCache);

        await service.GetEnrollments();

        // Act
        await service.DeleteEnrollment(99);
        await service.GetEnrollments();

        // Assert
        mockRepo.Verify(r => r.GetAllAsyncReadOnly(), Times.Exactly(2));
        mockRepo.Verify(r => r.GetByIdAsync(99), Times.Once);
        mockDistributedCache.Verify(
            c => c.RemoveAsync("course_student_counts", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetEnrollmentsByCourse_AfterSave_InvalidatesCourseKey()
    {
        // Arrange
        var mockRepo = new Mock<IEnrollmentRepository>();
        mockRepo.Setup(r => r.GetByCourseIdAsyncReadOnly(1))
                .ReturnsAsync(() => new List<Enrollment> { new() { Id = 1, CourseId = 1, StudentId = 2 } });
        mockRepo.Setup(r => r.AddAsync(It.IsAny<Enrollment>())).Returns(Task.CompletedTask);
        mockRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(mockRepo, memoryCache, CreateDistributedCacheMock());

        await service.GetEnrollmentsByCourse(1);

        // Act
        await service.SaveEnrollment(new Enrollment { Id = 0, CourseId = 1, StudentId = 2 });
        await service.GetEnrollmentsByCourse(1);

        // Assert
        mockRepo.Verify(r => r.GetByCourseIdAsyncReadOnly(1), Times.Exactly(2));
    }

    private static EnrollmentService CreateService(
        Mock<IEnrollmentRepository> repository,
        IMemoryCache memoryCache,
        Mock<IDistributedCache> distributedCache)
    {
        return new EnrollmentService(
            repository.Object,
            memoryCache,
            distributedCache.Object,
            NullLogger<EnrollmentService>.Instance);
    }

    private static Mock<IDistributedCache> CreateDistributedCacheMock()
    {
        var mock = new Mock<IDistributedCache>();

        mock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        mock.Setup(c => c.SetAsync(
                It.IsAny<string>(),
                It.IsAny<byte[]>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(c => c.RefreshAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return mock;
    }
}
