using Microsoft.EntityFrameworkCore;
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

        // Students enrolled in a specific course
        public List<Student> GetStudentsByCourse(int courseId)
        {
            return _dbContext.Enrollments
                .Where(e => e.CourseId == courseId)
                .Select(e => e.Student)
                .ToList();
        }

        public List<Course> GetCoursesByStudent(int studentId)
        {
            return _dbContext.Enrollments
                .Where(e => e.StudentId == studentId)
                .Select(e => e.Course)
                .ToList();
        }

        // Students enrolled in more than N courses
        //public List<Student> GetStudentsEnrolledInMoreThan(int courseCount)
        //{
        //    return _dbContext.Student
        //            .Select(s => new Student
        //            {
        //                Id = s.Id,
        //                Name = s.Name,
        //                Email = s.Email,
        //                Enrollments = _dbContext.Enrollments        // nested query
        //                    .Where(e => e.StudentId == s.Id)
        //                    .ToList()
        //            })
        //            .Where(s => s.Enrollments.Count > courseCount)
        //            .ToList();
        //}

        public List<Student> GetStudentsEnrolledInMoreThan(int courseCount)
        {
            return _dbContext.Student
                .Where(s => s.Enrollments.Count() > courseCount) // navigation property used here
                .Include(s => s.Enrollments)                     // optional: eager load enrollments
                .ThenInclude(e => e.Course)                      // optional: include the courses too
                .ToList();
        }

    }
}
