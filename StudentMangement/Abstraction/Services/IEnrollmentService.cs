using StudentMangement.Models;

namespace StudentMangement.Abstraction.Services
{
    public interface IEnrollmentService
    {
        Task<IEnumerable<Enrollment>> GetEnrollments();
        Task<Enrollment> GetEnrollmentById(int id);
        Task SaveEnrollment(Enrollment enrollment);
        Task DeleteEnrollment(int id);
        Task<IEnumerable<Enrollment>> GetEnrollmentsByCourse(int courseId);
        Task<List<Student>> GetStudentsInCourse(int courseId);
        Task<List<Course>> GetCoursesForStudent(int studentId);
    }
}
