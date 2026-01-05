using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Interfaces
{
    public interface IStudentService
    {
        Task<IEnumerable<Student>> GetStudentsAsync();
        Task<Student> GetStudentByIdAsync(int id);
        Task SaveStudentAsync(Student student);
        Task DeleteStudentAsync(int id);
        Task<List<Student>> GetStudentsEnrolledInMoreThan(int courseCount);
    }
}
