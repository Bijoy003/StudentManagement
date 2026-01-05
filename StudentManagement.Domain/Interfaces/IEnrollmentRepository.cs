using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Domain.Interfaces
{
    public interface IEnrollmentRepository
    {
        Task<IEnumerable<Enrollment>> GetAllAsync();
        Task<Enrollment> GetByIdAsync(int id);
        Task AddAsync(Enrollment enrollment);
        void Update(Enrollment enrollment);
        Task DeleteAsync(int id);
        Task SaveChangesAsync();

        Task<IEnumerable<Enrollment>> GetByCourseIdAsync(int courseId);

        // Students enrolled in a specific course
        Task<List<Student>> GetStudentsByCourseAsync(int courseId);

        Task<List<Course>> GetCoursesByStudentAsync(int studentId);

        // Students enrolled in more than N courses
        Task<List<Student>> GetStudentsEnrolledInMoreThanAsync(int courseCount);
    }
}
