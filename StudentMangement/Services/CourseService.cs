using StudentMangement.Abstraction.Repositories;
using StudentMangement.Abstraction.Services;
using StudentMangement.Models;
using System.Collections.Generic;

namespace StudentMangement.Services
{
    public class CourseService : ICourseService
    {
        private readonly ICourseRepository _courseRepository;

        public CourseService(ICourseRepository repo)
        {
            _courseRepository = repo;
        }

        public IEnumerable<Course> GetCourses()
        {
            return _courseRepository.GetAll();
        }

        public Course GetCourseById(int id)
        {
            return _courseRepository.GetById(id);
        }

        public void SaveCourse(Course course)
        {
            if (course.Id == 0)
            {
                _courseRepository.Add(course);
            }
            else
            {
                _courseRepository.Update(course);
            }
            _courseRepository.Save();
        }

        public void DeleteCourse(int id)
        {
            _courseRepository.Delete(id);
            _courseRepository.Save();
        }

        public List<CourseStudentCount> GetStudentCountPerCourse()
        {
            return _courseRepository.GetStudentCountPerCourse();
        }
    }
}
