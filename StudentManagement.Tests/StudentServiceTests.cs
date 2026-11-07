using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Tests
{
    public class StudentServiceTests
    {
        [Fact]
        public async Task GetStudentByIdAsync_ExistingId_ReturnsStudent()
        {
            // Arrange
            var mockStudentRepo = new Mock<IStudentRepository>();
            var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
            var mockCache = new Mock<IDistributedCache>();

            mockStudentRepo.Setup(r => r.GetByIdAsyncReadOnly(1))
                           .ReturnsAsync(new Student { Id = 1, Name = "John Doe" });

            var service = new StudentService(mockStudentRepo.Object, mockEnrollmentRepo.Object, mockCache.Object, NullLogger<StudentService>.Instance);

            // Act
            var student = await service.GetStudentByIdAsync(1);

            // Assert
            Assert.Equal("John Doe", student!.Name);
        }

        [Fact]
        public async Task GetStudentByIdAsync_NonExistingId_ThrowsException()
        {
            // Arrange
            var mockService = new Mock<IStudentService>();
            var mockStudentRepo = new Mock<IStudentRepository>();
            var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
            var mockCache = new Mock<IDistributedCache>();
            mockService.Setup(r => r.GetStudentByIdAsync(999)).ReturnsAsync((Student?)null);

            var service = new StudentService(mockStudentRepo.Object, mockEnrollmentRepo.Object, mockCache.Object, NullLogger<StudentService>.Instance);

            // Act & Assert
            Assert.ThrowsAsync<Exception>(() => service.GetStudentByIdAsync(999));
        }

        [Fact]
        public async Task Service_Should_Handle_Concurrent_Calls()
        {
            var mockStudentRepo = new Mock<IStudentRepository>();
            var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
            var mockCache = new Mock<IDistributedCache>();

            mockStudentRepo.Setup(r => r.GetByIdAsyncReadOnly(It.IsAny<int>()))
                           .ReturnsAsync(new Student { Id = 1, Name = "John" });

            var service = new StudentService(mockStudentRepo.Object, mockEnrollmentRepo.Object, mockCache.Object, NullLogger<StudentService>.Instance);

            var tasks = Enumerable.Range(0, 1000)
                                  .Select(_ => service.GetStudentByIdAsync(1));

            var results = await Task.WhenAll(tasks);

            Assert.All(results, r => Assert.Equal("John", r!.Name));
        }

        [Fact]
        public async Task Service_Waits_For_Repository()
        {
            var tcs = new TaskCompletionSource<Student?>();

            var mockRepo = new Mock<IStudentRepository>();
            var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
            var mockCache = new Mock<IDistributedCache>();

            mockRepo.Setup(r => r.GetByIdAsyncReadOnly(1))
                    .Returns(tcs.Task);

            var service = new StudentService(mockRepo.Object, mockEnrollmentRepo.Object, mockCache.Object, NullLogger<StudentService>.Instance);

            var task = service.GetStudentByIdAsync(1);

            Assert.False(task.IsCompleted);

            tcs.SetResult(new Student { Id = 1, Name = "John" });

            var result = await task;

            Assert.Equal("John", result?.Name);
        }
    }

}
