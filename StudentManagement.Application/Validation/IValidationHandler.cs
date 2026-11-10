using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Validation;

// Chain of Responsibility link for student validation:
//   * SetNext wires the following link and returns it so callers can chain fluently;
//   * ValidateAsync runs this rule, and ONLY when it passes forwards to the next link;
//   * returns null/whitespace when valid, otherwise the first failing rule's message
//     (short-circuit: downstream links are not invoked and no exception is thrown here);
//   * StudentService.SaveStudentAsync throws System.ComponentModel.DataAnnotations.
//     ValidationException with that message BEFORE touching the repository.
// Registration order defines the chain: RequiredFieldsRule -> EmailFormatRule -> DuplicateEmailRule.
public interface IValidationHandler
{
    IValidationHandler SetNext(IValidationHandler next);

    Task<string?> ValidateAsync(Student student);
}
