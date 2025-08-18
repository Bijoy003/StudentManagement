using StudentMangement.Abstraction.Repositories;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;

namespace StudentMangement.Services
{
    public class EnrollmentService : IEnrollmentService
    {
        private readonly IEnrollmentRepository _enrollmentRepository;

        public EnrollmentService(IEnrollmentRepository repo)
        {
            _enrollmentRepository = repo;
        }

        public IEnumerable<Enrollment> GetEnrollments()
        {
            return _enrollmentRepository.GetAll();
        }

        public Enrollment GetEnrollmentById(int id)
        {
            return _enrollmentRepository.GetById(id);
        }

        public void SaveEnrollment(Enrollment enrollment)
        {
            if (enrollment.Id == 0)
            {
                _enrollmentRepository.Add(enrollment);
            }
            else
            {
                _enrollmentRepository.Update(enrollment);
            }
            _enrollmentRepository.Save();
        }

        public void DeleteEnrollment(int id)
        {
            _enrollmentRepository.Delete(id);
            _enrollmentRepository.Save();
        }

        public IEnumerable<Enrollment> GetEnrollmentsByCourse(int courseId)
        {
            return _enrollmentRepository.GetByCourseId(courseId);
        }

        public List<Student> GetStudentsInCourse(int courseId)
        {
            return _enrollmentRepository.GetStudentsByCourse(courseId);
        }

        public List<Course> GetCoursesForStudent(int studentId)
        {
            return _enrollmentRepository.GetCoursesByStudent(studentId);
        }

    }
}
