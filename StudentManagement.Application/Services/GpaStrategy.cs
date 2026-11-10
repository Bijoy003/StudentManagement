using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Services;

public sealed class GpaStrategy : IGradingStrategy
{
    public GradeMode Mode => GradeMode.Gpa;

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
            >= 90m => "4.0",
            >= 80m => "3.0",
            >= 70m => "2.0",
            >= 60m => "1.0",
            _ => "0.0"
        };
    }
}
