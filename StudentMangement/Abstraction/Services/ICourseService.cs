using StudentMangement.Models;

namespace StudentMangement.Abstraction.Services
{
    public interface ICourseService
    {
        IEnumerable<Course> GetCourses();
        Course GetCourseById(int id);
        void SaveCourse(Course course);
        void DeleteCourse(int id);
        List<CourseStudentCountDto> GetStudentCountPerCourse();
    }
}
