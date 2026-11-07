namespace StudentManagement.Application.Evaluation.Models;

public sealed record EvaluationRun(
    string RunId,
    string PromptVersion,
    EvaluationConfig Config,
    IReadOnlyList<EvaluationResult> Results,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public static EvaluationRun Create(
        string promptVersion,
        EvaluationConfig config,
        IReadOnlyList<EvaluationResult> results) =>
        new(Guid.NewGuid().ToString(), promptVersion, config, results, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    public EvaluationSummary GetSummary() => new(
        RunId,
        PromptVersion,
        Results.Count,
        Results.Count(r => r.Metrics.PassesThresholds()),
        Results.Average(r => r.Metrics.Relevance),
        Results.Average(r => r.Metrics.Groundedness),
        Results.Average(r => r.Metrics.Correctness),
        Results.Average(r => r.Metrics.Hallucination),
        Results.Average(r => r.Metrics.LatencyMs),
        StartedAt,
        CompletedAt);

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["runId"] = RunId,
        ["promptVersion"] = PromptVersion,
        ["config"] = Config.ToDictionary(),
        ["summary"] = GetSummary().ToDictionary(),
        ["results"] = Results.Select(r => r.ToDictionary()),
        ["startedAt"] = StartedAt,
        ["completedAt"] = CompletedAt
    };
}

public sealed record EvaluationSummary(
    string RunId,
    string PromptVersion,
    int TotalQuestions,
    int PassedCount,
    double AvgRelevance,
    double AvgGroundedness,
    double AvgCorrectness,
    double AvgHallucination,
    double AvgLatencyMs,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public double PassRate => TotalQuestions > 0 ? (double)PassedCount / TotalQuestions : 0;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["runId"] = RunId,
        ["promptVersion"] = PromptVersion,
        ["totalQuestions"] = TotalQuestions,
        ["passedCount"] = PassedCount,
        ["passRate"] = Math.Round(PassRate, 4),
        ["avgRelevance"] = Math.Round(AvgRelevance, 4),
        ["avgGroundedness"] = Math.Round(AvgGroundedness, 4),
        ["avgCorrectness"] = Math.Round(AvgCorrectness, 4),
        ["avgHallucination"] = Math.Round(AvgHallucination, 4),
        ["avgLatencyMs"] = Math.Round(AvgLatencyMs, 2),
        ["startedAt"] = StartedAt,
        ["completedAt"] = CompletedAt
    };
}

public sealed record EvaluationConfig(
    int MaxRagChunks = 3,
    double SimilarityThreshold = 0.7,
    bool IncludeFullDocumentation = true,
    bool IncludeTools = true,
    string EmbeddingModel = "nomic-embed-text",
    double RelevanceWeight = 0.25,
    double GroundednessWeight = 0.30,
    double CorrectnessWeight = 0.25,
    double HallucinationWeight = 0.20,
    double RelevanceMin = 0.7,
    double GroundednessMin = 0.8,
    double HallucinationMax = 0.1,
    double CorrectnessMin = 0.7)
{
    public Dictionary<string, object> ToDictionary() => new()
    {
        ["maxRagChunks"] = MaxRagChunks,
        ["similarityThreshold"] = SimilarityThreshold,
        ["includeFullDocumentation"] = IncludeFullDocumentation,
        ["includeTools"] = IncludeTools,
        ["embeddingModel"] = EmbeddingModel,
        ["relevanceWeight"] = RelevanceWeight,
        ["groundednessWeight"] = GroundednessWeight,
        ["correctnessWeight"] = CorrectnessWeight,
        ["hallucinationWeight"] = HallucinationWeight,
        ["relevanceMin"] = RelevanceMin,
        ["groundednessMin"] = GroundednessMin,
        ["hallucinationMax"] = HallucinationMax,
        ["correctnessMin"] = CorrectnessMin
    };
}