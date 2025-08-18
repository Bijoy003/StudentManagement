using Microsoft.EntityFrameworkCore;
using StudentMangement.Models;

namespace StudentMangement.Abstraction.Repositories
{
    public interface IEnrollmentRepository
    {
        IEnumerable<Enrollment> GetAll();
        Enrollment GetById(int id);
        void Add(Enrollment enrollment);
        void Update(Enrollment enrollment);
        void Delete(int id);
        void Save();

        IEnumerable<Enrollment> GetByCourseId(int courseId);

        // Students enrolled in a specific course
        List<Student> GetStudentsByCourse(int courseId);

        List<Course> GetCoursesByStudent(int studentId);

        // Students enrolled in more than N courses
        List<Student> GetStudentsEnrolledInMoreThan(int courseCount);
    }
}
