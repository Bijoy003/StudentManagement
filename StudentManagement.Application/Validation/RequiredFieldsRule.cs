using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Validation;

// First chain link: Name and Email must be non-blank; a valid student is forwarded.
public sealed class RequiredFieldsRule : IValidationHandler
{
    private IValidationHandler? _next;

    public IValidationHandler SetNext(IValidationHandler next)
    {
        _next = next;
        return next;
    }

    public async Task<string?> ValidateAsync(Student student)
    {
        if (string.IsNullOrWhiteSpace(student.Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(student.Email))
        {
            return "Email is required.";
        }

        return _next is null ? null : await _next.ValidateAsync(student);
    }
}
