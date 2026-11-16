using StudentManagement.Domain.Entities;

namespace StudentManagement.Tests.Integration;

// Entities are built without Ids on purpose: the columns are IDENTITY, so SQL Server
// rejects explicit values unless IDENTITY_INSERT is on. Let the database assign keys
// and wire related rows through the FK properties after SaveChanges.
internal static class TestData
{
    public static Student Student(string name = "Ada Lovelace", string? email = null) =>
        new()
        {
            Name = name,
            Email = email ?? "ada@example.com"
        };

    public static Course Course(
        string name = "Algebra",
        int credits = 3,
        string? teacherId = "teacher-a",
        GradeMode gradeMode = GradeMode.Letter) =>
        new()
        {
            Name = name,
            Credits = credits,
            TeacherId = teacherId,
            GradeMode = gradeMode
        };

    // StudentId/CourseId are regular FK columns (not identity), so they can be set directly.
    public static Enrollment Enrollment(int studentId, int courseId, decimal? grade = null) =>
        new() { StudentId = studentId, CourseId = courseId, Grade = grade };
}
