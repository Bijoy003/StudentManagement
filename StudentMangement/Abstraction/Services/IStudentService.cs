using StudentMangement.Models;

namespace StudentMangement.Abstraction.Services
{
    public interface IStudentService
    {
        IEnumerable<Student> GetStudents();
        Student GetStudentById(int id);
        void SaveStudent(Student student);
        void DeleteStudent(int id);
        List<Student> GetStudentsEnrolledInMoreThan(int courseCount);
    }
}
