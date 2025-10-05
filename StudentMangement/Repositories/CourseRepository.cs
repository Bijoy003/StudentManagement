using Microsoft.EntityFrameworkCore;
using StudentMangement.Abstraction.Repositories;
using StudentMangement.Data;
using StudentMangement.Models;

namespace StudentMangement.Repositories
{
    public class CourseRepository : ICourseRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public CourseRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IEnumerable<Course>> GetAllAsync()
        {
            return await _dbContext.Courses.ToListAsync();
        }

        public async Task<Course> GetByIdAsync(int id)
        {
            return await _dbContext.Courses.FirstOrDefaultAsync(c => c.Id == id);
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




        //public List<CourseStudentCountDto> GetStudentCountPerCourse()
        //{
        //    return _dbContext.Enrollments
        //        .GroupBy(e => new { e.CourseId, e.Course.Name })
        //        .Select(g => new CourseStudentCountDto
        //        {
        //            CourseName = g.Key.Name,
        //            StudentCount = g.Count(),
        //            course = g.FirstOrDefault(),
        //        })
        //        .ToList();
        //}

    }

}
