using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.AI;
using StudentManagement.Application.Interfaces;

namespace StudentManagement.Application.Services
{
    public class ChatTools
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly IStudentService _studentService;
        private readonly ICourseService _courseService;
        private readonly IEnrollmentService _enrollmentService;

        public ChatTools(
            IStudentService studentService,
            ICourseService courseService,
            IEnrollmentService enrollmentService)
        {
            _studentService = studentService;
            _courseService = courseService;
            _enrollmentService = enrollmentService;
        }

        public IList<AITool> GetTools() =>
        [
            AIFunctionFactory.Create(ListStudentsAsync),
            AIFunctionFactory.Create(GetStudentByIdAsync),
            AIFunctionFactory.Create(GetStudentsEnrolledInMoreThanAsync),
            AIFunctionFactory.Create(ListCoursesAsync),
            AIFunctionFactory.Create(GetCourseByIdAsync),
            AIFunctionFactory.Create(GetStudentCountPerCourseAsync),
            AIFunctionFactory.Create(ListEnrollmentsAsync),
            AIFunctionFactory.Create(GetStudentsInCourseAsync),
            AIFunctionFactory.Create(GetCoursesForStudentAsync)
        ];

        [Description("Lists all students with id, name, email, phone, address, and enrollment date.")]
        public async Task<string> ListStudentsAsync(CancellationToken cancellationToken = default)
        {
            var students = await _studentService.GetStudentsAsync();
            var result = students.Select(s => new
            {
                s.Id,
                s.Name,
                s.Email,
                s.Phone,
                s.Address,
                DateOfEnroll = s.DateOfEnroll?.ToString("yyyy-MM-dd")
            });

            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [Description("Gets a single student by numeric id.")]
        public async Task<string> GetStudentByIdAsync(
            [Description("The student id.")] int studentId,
            CancellationToken cancellationToken = default)
        {
            var student = await _studentService.GetStudentByIdAsync(studentId);
            if (student is null)
            {
                return JsonSerializer.Serialize(new { error = $"No student found with id {studentId}." });
            }

            return JsonSerializer.Serialize(new
            {
                student.Id,
                student.Name,
                student.Email,
                student.Phone,
                student.Address,
                DateOfEnroll = student.DateOfEnroll?.ToString("yyyy-MM-dd")
            }, JsonOptions);
        }

        [Description("Finds students enrolled in more than the specified number of courses.")]
        public async Task<string> GetStudentsEnrolledInMoreThanAsync(
            [Description("Minimum number of courses; returns students enrolled in more than this count.")] int courseCount,
            CancellationToken cancellationToken = default)
        {
            var students = await _studentService.GetStudentsEnrolledInMoreThan(courseCount);
            var result = students.Select(s => new { s.Id, s.Name, s.Email });

            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [Description("Lists all courses with id, name, and credits.")]
        public async Task<string> ListCoursesAsync(CancellationToken cancellationToken = default)
        {
            var courses = await _courseService.GetAllCourses();
            var result = courses.Select(c => new { c.Id, c.Name, c.Credits });

            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [Description("Gets a single course by numeric id.")]
        public async Task<string> GetCourseByIdAsync(
            [Description("The course id.")] int courseId,
            CancellationToken cancellationToken = default)
        {
            var course = await _courseService.GetCourseById(courseId);
            if (course is null)
            {
                return JsonSerializer.Serialize(new { error = $"No course found with id {courseId}." });
            }

            return JsonSerializer.Serialize(new { course.Id, course.Name, course.Credits }, JsonOptions);
        }

        [Description("Returns how many students are enrolled in each course.")]
        public async Task<string> GetStudentCountPerCourseAsync(CancellationToken cancellationToken = default)
        {
            var counts = await _courseService.GetStudentCountPerCourse();
            var result = counts.Select(c => new { c.CourseName, c.StudentCount });

            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [Description("Lists all enrollments with student id, course id, and optional grade.")]
        public async Task<string> ListEnrollmentsAsync(CancellationToken cancellationToken = default)
        {
            var enrollments = await _enrollmentService.GetEnrollments();
            var result = enrollments.Select(e => new
            {
                e.Id,
                e.StudentId,
                StudentName = e.Student?.Name,
                e.CourseId,
                CourseName = e.Course?.Name,
                e.Grade
            });

            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [Description("Lists students enrolled in a specific course.")]
        public async Task<string> GetStudentsInCourseAsync(
            [Description("The course id.")] int courseId,
            CancellationToken cancellationToken = default)
        {
            var students = await _enrollmentService.GetStudentsInCourse(courseId);
            var result = students.Select(s => new { s.Id, s.Name, s.Email });

            return JsonSerializer.Serialize(result, JsonOptions);
        }

        [Description("Lists courses a specific student is enrolled in.")]
        public async Task<string> GetCoursesForStudentAsync(
            [Description("The student id.")] int studentId,
            CancellationToken cancellationToken = default)
        {
            var courses = await _enrollmentService.GetCoursesForStudent(studentId);
            var result = courses.Select(c => new { c.Id, c.Name, c.Credits });

            return JsonSerializer.Serialize(result, JsonOptions);
        }
    }
}
