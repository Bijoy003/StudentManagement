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
            var mockRepo = new Mock<IStudentService>();
            mockRepo.Setup(r => r.GetStudentByIdAsync(1))
                    .ReturnsAsync(new Student { Id = 1, Name = "John Doe" });

            var mockStudentRepo = new Mock<IStudentRepository>();
            var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();

            var service = new StudentService(mockStudentRepo.Object, mockEnrollmentRepo.Object);


            // Act
            var student = await service.GetStudentByIdAsync(1);

            // Assert
            Assert.Equal("John Doe", student.Name);
        }

        [Fact]
        public async Task GetStudentByIdAsync_NonExistingId_ThrowsException()
        {
            // Arrange
            var mockService = new Mock<IStudentService>();
            var mockStudentRepo = new Mock<IStudentRepository>();
            var mockEnrollmentRepo = new Mock<IEnrollmentRepository>();
            mockService.Setup(r => r.GetStudentByIdAsync(999)).ReturnsAsync((Student?)null);

            var service = new StudentService(mockStudentRepo.Object, mockEnrollmentRepo.Object);

            // Act & Assert
            Assert.ThrowsAsync<Exception>(() => service.GetStudentByIdAsync(999));
        }
    }

}
