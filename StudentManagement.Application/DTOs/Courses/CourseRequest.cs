using System.ComponentModel.DataAnnotations;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.DTOs.Courses;

// Create/update binding shape for /api/courses. Mirrors StudentRequest conventions:
// no Id (identity handled by the route), GradeMode accepted on input, TeacherId
// deliberately not bindable.
public sealed class CourseRequest
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, ErrorMessage = "Name cannot be longer than 100 characters")]
    public string? Name { get; set; }

    public int Credits { get; set; }

    public GradeMode GradeMode { get; set; } = GradeMode.Letter;
}
