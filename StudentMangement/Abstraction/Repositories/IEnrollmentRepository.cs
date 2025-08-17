using StudentMangement.Models;

namespace StudentMangement.Abstraction.Repositories
{
    public interface IEnrollmentRepository
    {
        IEnumerable<Enrollment> GetAll();
        Enrollment GetById(int id);
        void Add(Enrollment enrollment);
        void Update(Enrollment enrollment);
        void Delete(int id);
        void Save();

        IEnumerable<Enrollment> GetByCourseId(int courseId);
    }
}
