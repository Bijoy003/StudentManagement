using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Services;

public sealed class LetterGradeStrategy : IGradingStrategy
{
    public GradeMode Mode => GradeMode.Letter;

    public string ComputeGrade(decimal? score)
    {
        if (score is null)
        {
            return string.Empty;
        }

        if (score < 0m || score > 100m)
        {
            return "Invalid";
        }

        return score switch
        {
            >= 90m => "A",
            >= 80m => "B",
            >= 70m => "C",
            >= 60m => "D",
            _ => "F"
        };
    }
}
