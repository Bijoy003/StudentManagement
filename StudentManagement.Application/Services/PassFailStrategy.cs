using StudentManagement.Application.Interfaces;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Application.Services;

public sealed class PassFailStrategy : IGradingStrategy
{
    private const decimal PassingScore = 50m;

    public GradeMode Mode => GradeMode.PassFail;

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

        return score >= PassingScore ? "Pass" : "Fail";
    }
}
