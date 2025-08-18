using global::StudentMangement.Models;

namespace StudentMangement.Abstraction.Repositories
{
    public interface ICourseRepository
    {
        IEnumerable<Course> GetAll();
        Course GetById(int id);
        void Add(Course course);
        void Update(Course course);
        void Delete(int id);
        void Save();
        List<CourseStudentCount> GetStudentCountPerCourse();
    }
}
