using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Interfaces;

// Strategy pattern for grade computation/display: one implementation per GradeMode
// (LetterGradeStrategy, PassFailStrategy, GpaStrategy). Shared contract:
//   ComputeGrade(null)      -> ""        (no grade recorded yet)
//   score < 0 or > 100      -> "Invalid" (out-of-range input is never clamped or thrown)
// Letter bands: >=90 A, >=80 B, >=70 C, >=60 D, else F.
// PassFail: >=50 "Pass", else "Fail".
// Gpa: >=90 "4.0", >=80 "3.0", >=70 "2.0", >=60 "1.0", else "0.0".
public interface IGradingStrategy
{
    GradeMode Mode { get; }

    string ComputeGrade(decimal? score);
}
