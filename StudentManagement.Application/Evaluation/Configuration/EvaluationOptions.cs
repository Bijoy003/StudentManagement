namespace StudentManagement.Application.Evaluation.Configuration;

public sealed class EvaluationOptions
{
    public const string SectionName = "Evaluation";

    public bool Enabled { get; set; } = true;
    public string DefaultPromptVersion { get; set; } = "v1";
    public bool UseLlmJudge { get; set; } = true;
    public string JudgeModel { get; set; } = "qwen2.5:3b-instruct";
    public double JudgeTemperature { get; set; } = 0.0;
    public int JudgeMaxTokens { get; set; } = 500;

    public EvaluationMetricsThresholds Thresholds { get; set; } = new();
    public EvaluationMetricsWeights Weights { get; set; } = new();
}

public sealed class EvaluationMetricsThresholds
{
    public double RelevanceMin { get; set; } = 0.7;
    public double GroundednessMin { get; set; } = 0.8;
    public double HallucinationMax { get; set; } = 0.1;
    public double CorrectnessMin { get; set; } = 0.7;
}

public sealed class EvaluationMetricsWeights
{
    public double Relevance { get; set; } = 0.25;
    public double Groundedness { get; set; } = 0.30;
    public double Correctness { get; set; } = 0.25;
    public double Hallucination { get; set; } = 0.20;
}