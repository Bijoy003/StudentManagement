using StudentMangement.Models;

namespace StudentMangement.Abstraction.Services
{
    public interface ICourseService
    {
        Task<IEnumerable<Course>> GetAllCourses();
        Task<Course> GetCourseById(int id);
        Task SaveCourse(Course course);
        Task DeleteCourse(int id);
        Task<List<CourseStudentCountDto>> GetStudentCountPerCourse();
    }
}
