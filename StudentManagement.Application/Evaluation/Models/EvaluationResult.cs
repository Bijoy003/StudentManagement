namespace StudentManagement.Application.Evaluation.Models;

public sealed record EvaluationResult(
    string QuestionId,
    string Question,
    string GeneratedAnswer,
    IReadOnlyList<string> RetrievedContexts,
    EvaluationMetrics Metrics,
    string PromptVersion,
    DateTimeOffset EvaluatedAt)
{
    public static EvaluationResult Create(
        EvaluationQuestion question,
        string generatedAnswer,
        IReadOnlyList<string> retrievedContexts,
        EvaluationMetrics metrics,
        string promptVersion) =>
        new(question.Id, question.Question, generatedAnswer, retrievedContexts, metrics, promptVersion, DateTimeOffset.UtcNow);

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["questionId"] = QuestionId,
        ["question"] = Question,
        ["generatedAnswer"] = GeneratedAnswer,
        ["retrievedContexts"] = RetrievedContexts,
        ["metrics"] = Metrics.ToDictionary(),
        ["promptVersion"] = PromptVersion,
        ["evaluatedAt"] = EvaluatedAt
    };
}