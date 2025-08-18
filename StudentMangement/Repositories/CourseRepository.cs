using StudentMangement.Abstraction.Repositories;
using StudentMangement.Data;
using StudentMangement.Models;
using System.Collections.Generic;
using System.Linq;

namespace StudentMangement.Repositories
{
    public class CourseRepository : ICourseRepository
    {
        private readonly ApplicationDbContext _dbContext;

        public CourseRepository(ApplicationDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public IEnumerable<Course> GetAll()
        {
            return _dbContext.Courses.ToList();
        }

        public Course GetById(int id)
        {
            return _dbContext.Courses.FirstOrDefault(c => c.Id == id);
        }

        public void Add(Course course)
        {
            _dbContext.Courses.Add(course);
        }

        public void Update(Course course)
        {
            _dbContext.Courses.Update(course);
        }

        public void Delete(int id)
        {
            var course = GetById(id);
            if (course != null)
            {
                _dbContext.Courses.Remove(course);
            }
        }

        public void Save()
        {
            _dbContext.SaveChanges();
        }

        public List<CourseStudentCount> GetStudentCountPerCourse()
        {
            return _dbContext.Enrollments
                .GroupBy(e => new { e.CourseId, e.Course.Name })
                .Select(g => new CourseStudentCount
                {
                    CourseName = g.Key.Name,
                    StudentCount = g.Count()
                })
                .ToList();
        }

    }

}
