using System.ComponentModel.DataAnnotations;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Validation;

// Second chain link: Email must look like an address (same tolerance as
// EmailAddressAttribute). A null/blank email passes here — presence is
// RequiredFieldsRule's concern.
public sealed class EmailFormatRule : IValidationHandler
{
    private static readonly EmailAddressAttribute EmailAddress = new();

    private IValidationHandler? _next;

    public IValidationHandler SetNext(IValidationHandler next)
    {
        _next = next;
        return next;
    }

    public async Task<string?> ValidateAsync(Student student)
    {
        if (!string.IsNullOrWhiteSpace(student.Email) && !EmailAddress.IsValid(student.Email))
        {
            return "Email format is invalid.";
        }

        return _next is null ? null : await _next.ValidateAsync(student);
    }
}
