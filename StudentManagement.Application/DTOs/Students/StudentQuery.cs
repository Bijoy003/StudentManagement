namespace StudentManagement.Application.DTOs.Students;

public sealed class StudentQuery
{
    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    public string? Search { get; set; }

    public int? MinCourses { get; set; }
}
