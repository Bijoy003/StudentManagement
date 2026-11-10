using StudentManagement.Domain.Entities;
using StudentManagement.Domain.Interfaces;

namespace StudentManagement.Application.Validation;

// Third chain link: no OTHER student may already use the email. A student whose own Id
// matches the persisted row is not a duplicate (update scenario).
public sealed class DuplicateEmailRule : IValidationHandler
{
    private readonly IStudentRepository _studentRepository;
    private IValidationHandler? _next;

    public DuplicateEmailRule(IStudentRepository studentRepository)
    {
        _studentRepository = studentRepository;
    }

    public IValidationHandler SetNext(IValidationHandler next)
    {
        _next = next;
        return next;
    }

    public async Task<string?> ValidateAsync(Student student)
    {
        if (!string.IsNullOrWhiteSpace(student.Email))
        {
            var existingStudents = await _studentRepository.GetAllAsyncReadOnly();
            var takenByOther = existingStudents.Any(s =>
                s.Id != student.Id &&
                string.Equals(s.Email, student.Email, StringComparison.OrdinalIgnoreCase));

            if (takenByOther)
            {
                return $"Email '{student.Email}' is already registered to another student.";
            }
        }

        return _next is null ? null : await _next.ValidateAsync(student);
    }
}
