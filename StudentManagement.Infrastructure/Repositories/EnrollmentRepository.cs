using Microsoft.EntityFrameworkCore;
using StudentManagement.Infrastructure.Data;
using StudentMangement.Abstraction.Repositories;
using StudentMangement.Models;

namespace StudentManagement.Infrastructure.Repositories
{
    public class EnrollmentRepository : IEnrollmentRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public EnrollmentRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IEnumerable<Enrollment>> GetAllAsync()
        {
            return await _dbContext.Enrollments.ToListAsync();
        }

        public async Task<Enrollment> GetByIdAsync(int id)
        {
            return await _dbContext.Enrollments.FirstOrDefaultAsync(e => e.Id == id);
        }

        public async Task AddAsync(Enrollment enrollment)
        {
            await _dbContext.Enrollments.AddAsync(enrollment);
        }

        public void Update(Enrollment enrollment)
        {
            _dbContext.Enrollments.Update(enrollment);
        }

        public async Task DeleteAsync(int id)
        {
            var enrollment = await GetByIdAsync(id);
            if (enrollment != null)
            {
                _dbContext.Enrollments.Remove(enrollment);
            }
        }

        public async Task SaveChangesAsync()
        {
            await _dbContext.SaveChangesAsync();
        }

        public async Task<IEnumerable<Enrollment>> GetByCourseIdAsync(int courseId)
        {
            return await _dbContext.Enrollments
                             .Where(e => e.CourseId == courseId)
                             .ToListAsync();
        }

        // Students enrolled in a specific course
        public async Task<List<Student>> GetStudentsByCourseAsync(int courseId)
        {
            return await _dbContext.Enrollments
                .Where(e => e.CourseId == courseId)
                .Select(e => e.Student)
                .ToListAsync();
        }

        public async Task<List<Course>> GetCoursesByStudentAsync(int studentId)
        {
            return await _dbContext.Enrollments
                .Where(e => e.StudentId == studentId)
                .Select(e => e.Course)
                .ToListAsync();
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

        public async Task<List<Student>> GetStudentsEnrolledInMoreThanAsync(int courseCount)
        {
            return await _dbContext.Students
                .Where(s => s.Enrollments.Count() > courseCount) // navigation property used here
                .Include(s => s.Enrollments)                     // optional: eager load enrollments
                .ThenInclude(e => e.Course)                      // optional: include the courses too
                .ToListAsync();
        }

    }
}
