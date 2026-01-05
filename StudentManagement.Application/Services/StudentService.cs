using StudentManagement.Application.Interfaces;
using StudentMangement.Abstraction.Repositories;
using StudentMangement.Models;

namespace StudentManagement.Application.Services
{
    public class StudentService : IStudentService
    {
        private readonly IStudentRepository _studentRepository;
        private readonly IEnrollmentRepository _enrollmentRepository;

        public StudentService(IStudentRepository studentRepository, IEnrollmentRepository enrollmentRepository)
        {
            _studentRepository = studentRepository;
            _enrollmentRepository = enrollmentRepository;
        }

        public async Task<IEnumerable<Student>> GetStudentsAsync()
        {
            return await _studentRepository.GetAllAsync();
        }

        public async Task<Student> GetStudentByIdAsync(int id)
        {
            return await _studentRepository.GetByIdAsync(id);
        }

        public async Task SaveStudentAsync(Student student)
        {
            if (student.Id == 0)
            {
                await _studentRepository.AddAsync(student);
            }
            else
            {
                _studentRepository.Update(student);
            }
            await _studentRepository.SaveAsync();
        }

        public async Task DeleteStudentAsync(int id)
        {
            await _studentRepository.DeleteAsync(id);
            await _studentRepository.SaveAsync();
        }

        public async Task<List<Student>> GetStudentsEnrolledInMoreThan(int courseCount)
        {
            return await _enrollmentRepository.GetStudentsEnrolledInMoreThanAsync(courseCount);
        }
    }
}
