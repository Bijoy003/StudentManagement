using Microsoft.EntityFrameworkCore;
using StudentManagement.Domain.DTOs;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;
using StudentManagement.Infrastructure.Data;

namespace StudentManagement.Infrastructure.Repositories
{
    public class CourseRepository : ICourseRepository
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ReadOnlyDbContext _readOnlyDbContext;

        public CourseRepository(ApplicationDbContext dbContext, ReadOnlyDbContext readOnlyDbContext)
        {
            _dbContext = dbContext;
            _readOnlyDbContext = readOnlyDbContext;
        }

        public async Task<IEnumerable<Course>> GetAllAsync()
        {
            return await _dbContext.Courses.ToListAsync();
        }

        public async Task<IEnumerable<Course>> GetAllAsyncReadOnly()
        {
            return await _readOnlyDbContext.Courses.AsNoTracking().ToListAsync();
        }

        public async Task<Course?> GetByIdAsync(int id)
        {
            return await _dbContext.Courses.FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<Course?> GetByIdAsyncReadOnly(int id)
        {
            return await _readOnlyDbContext.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task AddAsync(Course course)
        {
            await _dbContext.Courses.AddAsync(course);
        }

        public void Update(Course course)
        {
            _dbContext.Courses.Update(course);
        }

        public async Task DeleteAsync(int id)
        {
            var course = await GetByIdAsync(id);
            if (course != null)
            {
                _dbContext.Courses.Remove(course);
            }
        }

        public async Task SaveChangesAsync()
        {
            await _dbContext.SaveChangesAsync();
        }

        public async Task<List<CourseStudentCountDto>> GetStudentCountPerCourseAsync()
        {
            return await _dbContext.Courses
                                .Join(_dbContext.Enrollments,
                                      c => c.Id,
                                      e => e.CourseId,
                                      (c, e) => new { c.Name, e.StudentId })
                                .GroupBy(x => x.Name)
                                .Select(g => new CourseStudentCountDto
                                {
                                    CourseName = g.Key,
                                    StudentCount = g.Count(),
                                    course = g.FirstOrDefault(),
                                })
                                .ToListAsync();
        }

        public async Task<List<CourseStudentCountDto>> GetStudentCountPerCourseAsyncReadOnly()
        {
            return await _readOnlyDbContext.Courses
                                .Join(_readOnlyDbContext.Enrollments,
                                      c => c.Id,
                                      e => e.CourseId,
                                      (c, e) => new { c.Name, e.StudentId })
                                .GroupBy(x => x.Name)
                                .Select(g => new CourseStudentCountDto
                                {
                                    CourseName = g.Key,
                                    StudentCount = g.Count(),
                                    course = g.FirstOrDefault(),
                                })
                                .AsNoTracking()
                                .ToListAsync();
        }
    }
}
