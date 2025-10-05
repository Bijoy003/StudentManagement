using StudentMangement.Models;

namespace StudentMangement.Abstraction.Services
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
