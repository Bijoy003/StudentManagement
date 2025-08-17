using StudentMangement.Models;

namespace StudentMangement.Abstraction.Repositories
{
    public interface IStudentRepository
    {
        IEnumerable<Student> GetAll();
        Student GetById(int id);
        void Add(Student student);
        void Update(Student student);
        void Delete(int id);
        void Save();
    }
}
