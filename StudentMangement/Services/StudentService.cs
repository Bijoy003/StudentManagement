using StudentMangement.Abstraction.Repositories;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;

namespace StudentMangement.Services
{
    public class StudentService : IStudentService
    {
        private readonly IStudentRepository _studentRepository;

        public StudentService(IStudentRepository studentRepository)
        {
            _studentRepository = studentRepository;
        }

        public IEnumerable<Student> GetStudents()
        {
            return _studentRepository.GetAll();
        }

        public Student GetStudentById(int id)
        {
            return _studentRepository.GetById(id);
        }

        public void SaveStudent(Student student)
        {
            if (student.Id == 0)
            {
                _studentRepository.Add(student);
            }
            else
            {
                _studentRepository.Update(student);
            }
            _studentRepository.Save();
        }

        public void DeleteStudent(int id)
        {
            _studentRepository.Delete(id);
            _studentRepository.Save();
        }
    }
}
