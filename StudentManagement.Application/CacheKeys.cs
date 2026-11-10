namespace StudentManagement.Application;

// Single source of truth for every distributed/in-process cache key. Values are
// byte-identical to the literals the services used before, so cross-service eviction
// keeps hitting the same entries.
public static class CacheKeys
{
    public const string AllCourses = "courses_all";
    public const string AllStudents = "students_all";
    public const string AllEnrollments = "enrollments_all";
    public const string CourseStudentCounts = "course_student_counts";

    public static string Course(int courseId) => $"course_{courseId}";

    public static string Student(int studentId) => $"student_{studentId}";

    public static string Enrollment(int enrollmentId) => $"enrollment_{enrollmentId}";

    public static string EnrollmentsByCourse(int courseId) => $"enrollment_course_{courseId}";

    public static string StudentsInCourse(int courseId) => $"enrollment_students_course_{courseId}";

    public static string CoursesForStudent(int studentId) => $"enrollment_courses_student_{studentId}";
}
