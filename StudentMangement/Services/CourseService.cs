using StudentMangement.Abstraction.Repositories;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;
using System.Collections.Generic;

namespace StudentMangement.Services
{
    public class CourseService : ICourseService
    {
        private readonly ICourseRepository _repo;

        public CourseService(ICourseRepository repo)
        {
            _repo = repo;
        }

        public IEnumerable<Course> GetCourses()
        {
            return _repo.GetAll();
        }

        public Course GetCourseById(int id)
        {
            return _repo.GetById(id);
        }

        public void SaveCourse(Course course)
        {
            if (course.Id == 0)
            {
                _repo.Add(course);
            }
            else
            {
                _repo.Update(course);
            }
            _repo.Save();
        }

        public void DeleteCourse(int id)
        {
            _repo.Delete(id);
            _repo.Save();
        }
    }
}
