namespace StudentManagement.Application.Evaluation.Models;

public sealed record EvaluationMetrics(
    double Relevance,
    double Groundedness,
    double Correctness,
    double Hallucination,
    double? Completeness = null,
    double? ToneScore = null,
    long LatencyMs = 0)
{
    public static EvaluationMetrics Empty => new(0, 0, 0, 1.0, 0, 0, 0);

    public double CompositeScore(
        double relevanceWeight = 0.25,
        double groundednessWeight = 0.30,
        double correctnessWeight = 0.25,
        double hallucinationWeight = 0.20) =>
        Relevance * relevanceWeight
        + Groundedness * groundednessWeight
        + Correctness * correctnessWeight
        + (1.0 - Hallucination) * hallucinationWeight;

    public bool PassesThresholds(
        double relevanceMin = 0.7,
        double groundednessMin = 0.8,
        double hallucinationMax = 0.1,
        double correctnessMin = 0.7) =>
        Relevance >= relevanceMin
        && Groundedness >= groundednessMin
        && Hallucination <= hallucinationMax
        && Correctness >= correctnessMin;

    public Dictionary<string, object> ToDictionary() => new()
    {
        ["relevance"] = Math.Round(Relevance, 4),
        ["groundedness"] = Math.Round(Groundedness, 4),
        ["correctness"] = Math.Round(Correctness, 4),
        ["hallucination"] = Math.Round(Hallucination, 4),
        ["completeness"] = Completeness.HasValue ? Math.Round(Completeness.Value, 4) : null,
        ["toneScore"] = ToneScore.HasValue ? Math.Round(ToneScore.Value, 4) : null,
        ["latencyMs"] = LatencyMs,
        ["compositeScore"] = Math.Round(CompositeScore(), 4),
        ["passes"] = PassesThresholds()
    };
}