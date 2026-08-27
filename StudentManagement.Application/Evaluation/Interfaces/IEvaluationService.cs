using StudentManagement.Application.Evaluation.Datasets;
using StudentManagement.Application.Evaluation.Models;

namespace StudentManagement.Application.Evaluation.Interfaces;

public interface IEvaluationService
{
    Task<EvaluationRun> RunEvaluationAsync(
        EvaluationDataset dataset,
        EvaluationConfig config,
        string promptVersion,
        CancellationToken cancellationToken = default);

    Task<EvaluationResult> EvaluateSingleAsync(
        EvaluationQuestion question,
        EvaluationConfig config,
        string promptVersion,
        CancellationToken cancellationToken = default);

    Task<ExperimentResult> RunExperimentAsync(
        PromptExperiment experiment,
        EvaluationDataset dataset,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvaluationRun>> GetRunHistoryAsync(int maxRuns = 50, CancellationToken cancellationToken = default);

    Task<EvaluationRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default);
}

public interface IEvaluationDatasetProvider
{
    Task<EvaluationDataset> GetDatasetAsync(CancellationToken cancellationToken = default);
    Task<EvaluationDataset> GetDatasetByCategoryAsync(string category, CancellationToken cancellationToken = default);
}

public interface ILlmJudgeService
{
    Task<JudgeResult> JudgeCorrectnessAsync(
        string question,
        string expectedAnswer,
        string generatedAnswer,
        CancellationToken cancellationToken = default);

    Task<JudgeResult> JudgeHallucinationAsync(
        string question,
        string generatedAnswer,
        IReadOnlyList<string> contexts,
        CancellationToken cancellationToken = default);

    Task<JudgeResult> JudgeCompletenessAsync(
        string question,
        string generatedAnswer,
        CancellationToken cancellationToken = default);

    Task<JudgeResult> JudgeToneAsync(
        string question,
        string generatedAnswer,
        CancellationToken cancellationToken = default);
}

public interface IPromptManager
{
    Task<PromptTemplate> GetPromptAsync(string version, CancellationToken cancellationToken = default);
    Task<PromptTemplate> RegisterPromptAsync(PromptTemplate prompt, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PromptTemplate>> GetAllPromptsAsync(CancellationToken cancellationToken = default);
    Task<PromptTemplate> GetCurrentPromptAsync(CancellationToken cancellationToken = default);
    Task SetCurrentPromptAsync(string version, CancellationToken cancellationToken = default);
}

public sealed record JudgeResult(
    double Score,
    string Reasoning,
    bool Passed,
    Dictionary<string, object>? Metadata = null)
{
    public static JudgeResult Fail(string reasoning) => new(0, reasoning, false);
    public static JudgeResult Pass(double score, string reasoning) => new(score, reasoning, true);
}