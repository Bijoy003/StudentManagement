namespace StudentManagement.Application.Evaluation.Models;

public sealed record EvaluationQuestion(
    string Id,
    string Question,
    string ExpectedAnswer,
    string ExpectedContext,
    string Category,
    EvaluationDifficulty Difficulty = EvaluationDifficulty.Medium,
    IReadOnlyList<string>? Tags = null)
{
    public static EvaluationQuestion Create(
        string question,
        string expectedAnswer,
        string expectedContext,
        string category,
        EvaluationDifficulty difficulty = EvaluationDifficulty.Medium,
        params string[] tags) =>
        new(Guid.NewGuid().ToString(), question, expectedAnswer, expectedContext, category, difficulty, tags);
}

public enum EvaluationDifficulty
{
    Easy = 1,
    Medium = 2,
    Hard = 3,
    Adversarial = 4
}