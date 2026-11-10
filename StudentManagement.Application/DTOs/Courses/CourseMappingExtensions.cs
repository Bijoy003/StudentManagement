using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.DTOs.Courses;

public static class CourseMappingExtensions
{
    public static CourseDto ToDto(this Course course) => new(
        course.Id,
        course.Name,
        course.Credits,
        course.TeacherId,
        course.GradeMode);

    public static Course ToEntity(this CourseRequest request) => new()
    {
        Name = Normalize(request.Name) ?? string.Empty,
        Credits = request.Credits,
        GradeMode = request.GradeMode
    };

    // Mutates Name/Credits/GradeMode only; Id, TeacherId and Enrollments are never
    // bound or touched through the DTO.
    public static void ApplyTo(this CourseRequest request, Course course)
    {
        course.Name = Normalize(request.Name) ?? string.Empty;
        course.Credits = request.Credits;
        course.GradeMode = request.GradeMode;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
