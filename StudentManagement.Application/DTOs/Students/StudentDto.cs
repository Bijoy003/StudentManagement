namespace StudentManagement.Application.DTOs.Students;

public sealed record StudentDto(
    int Id,
    string? Name,
    string? Email,
    string? Phone,
    string? Address,
    DateTime? DateOfEnroll);
