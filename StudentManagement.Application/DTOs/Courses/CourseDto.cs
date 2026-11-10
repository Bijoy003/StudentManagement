using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.DTOs.Courses;

// Read shape for /api/courses. GradeMode is exposed here (and only through DTOs) so
// REST never serializes the Course entity.
public sealed record CourseDto(
    int Id,
    string? Name,
    int Credits,
    string? TeacherId,
    GradeMode GradeMode);
