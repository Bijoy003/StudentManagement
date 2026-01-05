using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Application.Services
{
    public class EnrollmentService : IEnrollmentService
    {
        private readonly IEnrollmentRepository _enrollmentRepository;

        public EnrollmentService(IEnrollmentRepository repo)
        {
            _enrollmentRepository = repo;
        }

        public async Task<IEnumerable<Enrollment>> GetEnrollments()
        {
            return await _enrollmentRepository.GetAllAsync();
        }

        public async Task<Enrollment> GetEnrollmentById(int id)
        {
            return await _enrollmentRepository.GetByIdAsync(id);
        }

        public async Task SaveEnrollment(Enrollment enrollment)
        {
            if (enrollment.Id == 0)
            {
                await _enrollmentRepository.AddAsync(enrollment);
            }
            else
            {
                _enrollmentRepository.Update(enrollment);
            }
            await _enrollmentRepository.SaveChangesAsync();
        }

        public async Task DeleteEnrollment(int id)
        {
            await _enrollmentRepository.DeleteAsync(id);
            await _enrollmentRepository.SaveChangesAsync();
        }

        public async Task<IEnumerable<Enrollment>> GetEnrollmentsByCourse(int courseId)
        {
            return await _enrollmentRepository.GetByCourseIdAsync(courseId);
        }

        public async Task<List<Student>> GetStudentsInCourse(int courseId)
        {
            return await _enrollmentRepository.GetStudentsByCourseAsync(courseId);
        }

        public async Task<List<Course>> GetCoursesForStudent(int studentId)
        {
            return await _enrollmentRepository.GetCoursesByStudentAsync(studentId);
        }

    }
}
