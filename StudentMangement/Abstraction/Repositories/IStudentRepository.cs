using StudentMangement.Models;

namespace StudentMangement.Abstraction.Repositories
{
    public interface IStudentRepository
    {
        Task<IEnumerable<Student>> GetAllAsync();
        Task<Student> GetByIdAsync(int id);
        Task AddAsync(Student student);
        void Update(Student student);
        Task DeleteAsync(int id);
        Task SaveAsync();
    }
}
