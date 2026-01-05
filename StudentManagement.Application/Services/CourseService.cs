using StudentManagement.Application.DTOs;
using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Application.Services
{
    public class CourseService : ICourseService
    {
        private readonly ICourseRepository _courseRepository;

        public CourseService(ICourseRepository repo)
        {
            _courseRepository = repo;
        }

        public async Task<IEnumerable<Course>> GetAllCourses()
        {
            return await _courseRepository.GetAllAsync();
        }

        public async Task<Course> GetCourseById(int id)
        {
            return await _courseRepository.GetByIdAsync(id);
        }

        public async Task SaveCourse(Course course)
        {
            if (course.Id == 0)
            {
                await _courseRepository.AddAsync(course);
            }
            else
            {
                _courseRepository.Update(course);
            }
            await _courseRepository.SaveChangesAsync();
        }

        public async Task DeleteCourse(int id)
        {
            await _courseRepository.DeleteAsync(id);
            await _courseRepository.SaveChangesAsync();
        }

        public async Task<List<CourseStudentCountDto>> GetStudentCountPerCourse()
        {
            return await _courseRepository.GetStudentCountPerCourseAsync();
        }
    }
}
