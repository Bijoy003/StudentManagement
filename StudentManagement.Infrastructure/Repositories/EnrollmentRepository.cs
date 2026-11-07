using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Infrastructure.Repositories
{
    public class EnrollmentRepository : IEnrollmentRepository
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ReadOnlyDbContext _readOnlyDbContext;

        public EnrollmentRepository(ApplicationDbContext dbContext, ReadOnlyDbContext readOnlyDbContext)
        {
            _dbContext = dbContext;
            _readOnlyDbContext = readOnlyDbContext;
        }

        public async Task<IEnumerable<Enrollment>> GetAllAsync()
        {
            return await _dbContext.Enrollments.ToListAsync();
        }

        public async Task<IEnumerable<Enrollment>> GetAllAsyncReadOnly()
        {
            // The index view renders Student/Course names, so the navigations must be loaded explicitly
            // (AsNoTracking disables the identity-resolution fixup that used to populate them).
            return await _readOnlyDbContext.Enrollments
                .Include(e => e.Student)
                .Include(e => e.Course)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<Enrollment?> GetByIdAsync(int id)
        {
            return await _dbContext.Enrollments.FirstOrDefaultAsync(e => e.Id == id);
        }

        public async Task<Enrollment?> GetByIdAsyncReadOnly(int id)
        {
            return await _readOnlyDbContext.Enrollments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
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

        public async Task<IEnumerable<Enrollment>> GetByCourseIdAsyncReadOnly(int courseId)
        {
            return await _readOnlyDbContext.Enrollments
                             .Where(e => e.CourseId == courseId)
                             .AsNoTracking()
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

        public async Task<List<Student>> GetStudentsByCourseAsyncReadOnly(int courseId)
        {
            return await _readOnlyDbContext.Enrollments
                .Where(e => e.CourseId == courseId)
                .Select(e => e.Student)
                .AsNoTracking()
                .ToListAsync();
        }

        public async Task<List<Course>> GetCoursesByStudentAsync(int studentId)
        {
            return await _dbContext.Enrollments
                .Where(e => e.StudentId == studentId)
                .Select(e => e.Course)
                .ToListAsync();
        }

        public async Task<List<Course>> GetCoursesByStudentAsyncReadOnly(int studentId)
        {
            return await _readOnlyDbContext.Enrollments
                .Where(e => e.StudentId == studentId)
                .Select(e => e.Course)
                .AsNoTracking()
                .ToListAsync();
        }

        // Students enrolled in more than N courses
        public async Task<List<Student>> GetStudentsEnrolledInMoreThanAsync(int courseCount)
        {
            return await _dbContext.Students
                .Where(s => s.Enrollments.Count() > courseCount)
                .Include(s => s.Enrollments)
                .ThenInclude(e => e.Course)
                .ToListAsync();
        }

        public async Task<List<Student>> GetStudentsEnrolledInMoreThanAsyncReadOnly(int courseCount)
        {
            return await _readOnlyDbContext.Students
                .Where(s => s.Enrollments.Count() > courseCount)
                .Include(s => s.Enrollments)
                .ThenInclude(e => e.Course)
                .AsNoTracking()
                .ToListAsync();
        }
    }
}
