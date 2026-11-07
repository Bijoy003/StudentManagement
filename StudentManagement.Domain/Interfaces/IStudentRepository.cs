using StudentManagement.Domain.Entities;

namespace StudentManagement.Domain.Interfaces
{
    public interface IStudentRepository
    {
        Task<IEnumerable<Student>> GetAllAsync();
        Task<IEnumerable<Student>> GetAllAsyncReadOnly();
        Task<Student?> GetByIdAsync(int id);
        Task<Student?> GetByIdAsyncReadOnly(int id);
        Task AddAsync(Student student);
        void Update(Student student);
        Task DeleteAsync(int id);
        Task SaveAsync();
    }
}
