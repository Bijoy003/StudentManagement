using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.DTOs.Students;

public static class StudentMappingExtensions
{
    public static StudentDto ToDto(this Student student) => new(
        student.Id,
        student.Name,
        student.Email,
        student.Phone,
        student.Address,
        student.DateOfEnroll);

    public static Student ToEntity(this StudentRequest request) => new()
    {
        Name = Normalize(request.Name),
        Email = Normalize(request.Email),
        Phone = Normalize(request.Phone),
        Address = Normalize(request.Address),
        DateOfEnroll = request.DateOfEnroll
    };

    public static void ApplyTo(this StudentRequest request, Student student)
    {
        student.Name = Normalize(request.Name);
        student.Email = Normalize(request.Email);
        student.Phone = Normalize(request.Phone);
        student.Address = Normalize(request.Address);
        student.DateOfEnroll = request.DateOfEnroll;
    }

    public static void ApplyTo(this StudentPatchRequest request, Student student)
    {
        if (request.Name is not null)
            student.Name = Normalize(request.Name);

        if (request.Email is not null)
            student.Email = Normalize(request.Email);

        if (request.Phone is not null)
            student.Phone = Normalize(request.Phone);

        if (request.Address is not null)
            student.Address = Normalize(request.Address);

        if (request.DateOfEnroll is not null)
            student.DateOfEnroll = request.DateOfEnroll;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
