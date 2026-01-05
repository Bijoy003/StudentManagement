using StudentMangement.Models;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Domain.Interfaces
{
    public interface ICourseRepository
    {
        Task<IEnumerable<Course>> GetAllAsync();
        Task<Course> GetByIdAsync(int id);
        Task AddAsync(Course course);
        void Update(Course course);
        Task DeleteAsync(int id);
        Task SaveChangesAsync();
        Task<List<CourseStudentCountDto>> GetStudentCountPerCourseAsync();
    }
}
