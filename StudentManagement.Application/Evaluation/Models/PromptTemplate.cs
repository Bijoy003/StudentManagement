namespace StudentManagement.Application.Evaluation.Models;

public sealed record PromptTemplate(
    string Version,
    string Name,
    string Template,
    string Description,
    DateTimeOffset CreatedAt,
    IReadOnlyDictionary<string, string>? Variables = null)
{
    public static PromptTemplate Create(
        string version,
        string name,
        string template,
        string description,
        IReadOnlyDictionary<string, string>? variables = null) =>
        new(version, name, template, description, DateTimeOffset.UtcNow, variables);

    public string Render(IReadOnlyDictionary<string, string> values)
    {
        var result = Template;
        foreach (var kvp in values)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value, StringComparison.Ordinal);
        }
        return result;
    }

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["version"] = Version,
        ["name"] = Name,
        ["template"] = Template,
        ["description"] = Description,
        ["createdAt"] = CreatedAt,
        ["variables"] = Variables ?? new Dictionary<string, string>()
    };
}

public sealed record PromptExperiment(
    string Id,
    string Name,
    string Description,
    string BaselinePromptVersion,
    string VariantPromptVersion,
    IReadOnlyList<string> QuestionIds,
    EvaluationConfig Config,
    DateTimeOffset CreatedAt)
{
    public static PromptExperiment Create(
        string name,
        string description,
        string baselinePromptVersion,
        string variantPromptVersion,
        EvaluationConfig config,
        params string[] questionIds) =>
        new(Guid.NewGuid().ToString(), name, description, baselinePromptVersion, variantPromptVersion, questionIds, config, DateTimeOffset.UtcNow);

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["id"] = Id,
        ["name"] = Name,
        ["description"] = Description,
        ["baselinePromptVersion"] = BaselinePromptVersion,
        ["variantPromptVersion"] = VariantPromptVersion,
        ["questionIds"] = QuestionIds,
        ["config"] = Config.ToDictionary(),
        ["createdAt"] = CreatedAt
    };
}

public sealed record ExperimentResult(
    string ExperimentId,
    EvaluationRun BaselineRun,
    EvaluationRun VariantRun,
    StatisticalComparison Comparison,
    DateTimeOffset CompletedAt)
{
    public Dictionary<string, object> ToDictionary() => new()
    {
        ["experimentId"] = ExperimentId,
        ["baselineRun"] = BaselineRun.ToDictionary(),
        ["variantRun"] = VariantRun.ToDictionary(),
        ["comparison"] = Comparison.ToDictionary(),
        ["completedAt"] = CompletedAt
    };
}

public sealed record StatisticalComparison(
    double RelevanceDelta,
    double GroundednessDelta,
    double CorrectnessDelta,
    double HallucinationDelta,
    bool IsSignificant,
    double PValue,
    int SampleSize,
    Dictionary<string, double> PerQuestionDeltas)
{
    public Dictionary<string, object> ToDictionary() => new()
    {
        ["relevanceDelta"] = Math.Round(RelevanceDelta, 4),
        ["groundednessDelta"] = Math.Round(GroundednessDelta, 4),
        ["correctnessDelta"] = Math.Round(CorrectnessDelta, 4),
        ["hallucinationDelta"] = Math.Round(HallucinationDelta, 4),
        ["isSignificant"] = IsSignificant,
        ["pValue"] = Math.Round(PValue, 4),
        ["sampleSize"] = SampleSize,
        ["perQuestionDeltas"] = PerQuestionDeltas.ToDictionary(kvp => kvp.Key, kvp => Math.Round(kvp.Value, 4))
    };
}