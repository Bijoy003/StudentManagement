using StudentMangement.Abstraction.Repositories;
using StudentMangement.Data;
using StudentMangement.Models;

namespace StudentMangement.Repositories
{
    public class EnrollmentRepository : IEnrollmentRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public EnrollmentRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public IEnumerable<Enrollment> GetAll()
        {
            return _dbContext.Enrollments.ToList();
        }

        public Enrollment GetById(int id)
        {
            return _dbContext.Enrollments.FirstOrDefault(e => e.Id == id);
        }

        public void Add(Enrollment enrollment)
        {
            _dbContext.Enrollments.Add(enrollment);
        }

        public void Update(Enrollment enrollment)
        {
            _dbContext.Enrollments.Update(enrollment);
        }

        public void Delete(int id)
        {
            var enrollment = GetById(id);
            if (enrollment != null)
            {
                _dbContext.Enrollments.Remove(enrollment);
            }
        }

        public void Save()
        {
            _dbContext.SaveChanges();
        }

        public IEnumerable<Enrollment> GetByCourseId(int courseId)
        {
            return _dbContext.Enrollments
                             .Where(e => e.CourseId == courseId)
                             .ToList();
        }
    }
}
