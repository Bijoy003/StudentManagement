using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests;

public class CourseServiceTests
{
    private static CourseService CreateService(
        Mock<ICourseRepository> courseRepo,
        Mock<IDistributedCache> cache)
    {
        // Loose cache: SaveCourse only evicts keys; Remove calls must not throw.
        return new CourseService(
            courseRepo.Object,
            cache.Object,
            NullLogger<CourseService>.Instance);
    }

    [Fact]
    public async Task SaveCourse_UpdatePath_PreservesPersistedTeacherId()
    {
        // Arrange - persisted owner is teacher-a; the bound payload tries teacher-b
        var mockRepo = new Mock<ICourseRepository>();
        var mockCache = new Mock<IDistributedCache>();
        mockRepo.Setup(r => r.GetByIdAsyncReadOnly(5))
                .ReturnsAsync(new Course { Id = 5, Name = "Algebra", Credits = 3, TeacherId = "teacher-a" });

        Course? updated = null;
        mockRepo.Setup(r => r.Update(It.IsAny<Course>())).Callback<Course>(c => updated = c);

        var service = CreateService(mockRepo, mockCache);
        var submitted = new Course
        {
            Id = 5,
            Name = "Algebra II",
            Credits = 4,
            TeacherId = "teacher-b",
            Enrollments = new List<Enrollment> { new() { Id = 99, CourseId = 5, StudentId = 11 } }
        };

        // Act
        await service.SaveCourse(submitted);

        // Assert - mass-assignment refused: the persisted owner is restamped and any
        // crafted enrollment graph is dropped before the entity reaches Update
        mockRepo.Verify(r => r.Update(It.IsAny<Course>()), Times.Once);
        mockRepo.Verify(r => r.AddAsync(It.IsAny<Course>()), Times.Never);
        mockRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.NotNull(updated);
        Assert.Equal("teacher-a", updated!.TeacherId);
        Assert.Empty(updated.Enrollments);
    }

    [Fact]
    public async Task SaveCourse_NewCourse_IgnoresBoundTeacherId()
    {
        // Arrange - the legacy UI never assigns teachers; ownership is stamped only
        // through TeacherService, so a bound TeacherId must be discarded
        var mockRepo = new Mock<ICourseRepository>();
        var mockCache = new Mock<IDistributedCache>();

        Course? added = null;
        mockRepo.Setup(r => r.AddAsync(It.IsAny<Course>())).Callback<Course>(c => added = c).Returns(Task.CompletedTask);

        var service = CreateService(mockRepo, mockCache);
        var submitted = new Course
        {
            Id = 0,
            Name = "New Course",
            Credits = 3,
            TeacherId = "x",
            Enrollments = new List<Enrollment> { new() { Id = 99, CourseId = 0, StudentId = 11 } }
        };

        // Act
        await service.SaveCourse(submitted);

        // Assert
        mockRepo.Verify(r => r.AddAsync(It.IsAny<Course>()), Times.Once);
        mockRepo.Verify(r => r.Update(It.IsAny<Course>()), Times.Never);
        mockRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        Assert.NotNull(added);
        Assert.Null(added!.TeacherId);
        Assert.Empty(added.Enrollments);
    }
}
