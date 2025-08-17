using StudentMangement.Abstraction.Repositories;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;

namespace StudentMangement.Services
{
    public class EnrollmentService : IEnrollmentService
    {
        private readonly IEnrollmentRepository _repo;

        public EnrollmentService(IEnrollmentRepository repo)
        {
            _repo = repo;
        }

        public IEnumerable<Enrollment> GetEnrollments()
        {
            return _repo.GetAll();
        }

        public Enrollment GetEnrollmentById(int id)
        {
            return _repo.GetById(id);
        }

        public void SaveEnrollment(Enrollment enrollment)
        {
            if (enrollment.Id == 0)
            {
                _repo.Add(enrollment);
            }
            else
            {
                _repo.Update(enrollment);
            }
            _repo.Save();
        }

        public void DeleteEnrollment(int id)
        {
            _repo.Delete(id);
            _repo.Save();
        }

        public IEnumerable<Enrollment> GetEnrollmentsByCourse(int courseId)
        {
            return _repo.GetByCourseId(courseId);
        }
    }
}
