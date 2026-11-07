using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Interfaces;

public interface ITeacherService
{
    Task<List<Course>> GetTeacherCoursesAsync(string teacherId);
    Task<Course?> GetCourseByIdAsync(int courseId);
    Task<bool> SaveCourseAsync(Course course, string teacherId);
    Task<bool> DeleteCourseAsync(int courseId, string teacherId);
    Task<List<Enrollment>> GetEnrollmentsByCourseAsync(int courseId);
    Task<bool> UpdateGradeAsync(int enrollmentId, decimal? grade, string teacherId);
}
