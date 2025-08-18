using StudentMangement.Models;

namespace StudentMangement.Abstraction.Services
{
    public interface IEnrollmentService
    {
        IEnumerable<Enrollment> GetEnrollments();
        Enrollment GetEnrollmentById(int id);
        void SaveEnrollment(Enrollment enrollment);
        void DeleteEnrollment(int id);
        IEnumerable<Enrollment> GetEnrollmentsByCourse(int courseId);

        List<Student> GetStudentsInCourse(int courseId);

        List<Course> GetCoursesForStudent(int studentId);
    }
}
