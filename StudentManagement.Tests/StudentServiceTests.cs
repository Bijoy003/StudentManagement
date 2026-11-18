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

        [Fact]
        public async Task SaveStudentAsync_ExistingStudent_UpdatesAndInvalidatesCaches()
        {
            // Arrange — Id set means the row already exists, so the update path must be used
            var mockStudentRepo = new Mock<IStudentRepository>();
            var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
            var mockCache = new Mock<IDistributedCache>();

            Student? updated = null;
            mockStudentRepo.Setup(r => r.Update(It.IsAny<Student>())).Callback<Student>(s => updated = s);

            var service = new StudentService(mockStudentRepo.Object, mockEnrollmentRepo.Object, mockCache.Object, NullLogger<StudentService>.Instance);
            var student = new Student { Id = 7, Name = "Ada King", Email = "ada@example.com" };

            // Act
            await service.SaveStudentAsync(student);

            // Assert
            Assert.Same(student, updated);
            mockStudentRepo.Verify(r => r.Update(It.IsAny<Student>()), Times.Once);
            mockStudentRepo.Verify(r => r.AddAsync(It.IsAny<Student>()), Times.Never);
            mockStudentRepo.Verify(r => r.SaveAsync(), Times.Once);
            mockCache.Verify(c => c.RemoveAsync("students_all", It.IsAny<CancellationToken>()), Times.Once);
            mockCache.Verify(c => c.RemoveAsync("student_7", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SaveStudentAsync_NewStudent_AddsAndInvalidatesCaches()
        {
            // Arrange — Id 0 means a new row; the database assigns the Id (see the
            // SqlServer integration test for the identity assertion itself)
            var mockStudentRepo = new Mock<IStudentRepository>();
            var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
            var mockCache = new Mock<IDistributedCache>();

            Student? added = null;
            mockStudentRepo.Setup(r => r.AddAsync(It.IsAny<Student>())).Callback<Student>(s => added = s).Returns(Task.CompletedTask);

            var service = new StudentService(mockStudentRepo.Object, mockEnrollmentRepo.Object, mockCache.Object, NullLogger<StudentService>.Instance);
            var student = new Student { Name = "Ada Lovelace", Email = "ada@example.com" };

            // Act
            await service.SaveStudentAsync(student);

            // Assert
            Assert.Same(student, added);
            mockStudentRepo.Verify(r => r.AddAsync(It.IsAny<Student>()), Times.Once);
            mockStudentRepo.Verify(r => r.Update(It.IsAny<Student>()), Times.Never);
            mockStudentRepo.Verify(r => r.SaveAsync(), Times.Once);
            mockCache.Verify(c => c.RemoveAsync("students_all", It.IsAny<CancellationToken>()), Times.Once);
            mockCache.Verify(c => c.RemoveAsync("student_0", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task DeleteStudentAsync_Student_DeletesAndInvalidatesCaches()
        {
            // Arrange
            var mockStudentRepo = new Mock<IStudentRepository>();
            var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
            var mockCache = new Mock<IDistributedCache>();

            var service = new StudentService(mockStudentRepo.Object, mockEnrollmentRepo.Object, mockCache.Object, NullLogger<StudentService>.Instance);

            // Act — the enrollment cascade is a database concern, asserted by
            // DeleteStudentAsync_StudentWithEnrollments_ReturnsEnrollmentsCascaded
            await service.DeleteStudentAsync(7);

            // Assert
            mockStudentRepo.Verify(r => r.DeleteAsync(7), Times.Once);
            mockStudentRepo.Verify(r => r.SaveAsync(), Times.Once);
            mockCache.Verify(c => c.RemoveAsync("students_all", It.IsAny<CancellationToken>()), Times.Once);
            mockCache.Verify(c => c.RemoveAsync("student_7", It.IsAny<CancellationToken>()), Times.Once);
        }
    }

}
