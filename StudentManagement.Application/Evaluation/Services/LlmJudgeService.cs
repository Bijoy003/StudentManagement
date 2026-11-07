using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Evaluation.Interfaces;
using StudentManagement.Application.Evaluation.Models;

namespace StudentManagement.Application.Evaluation.Services;

public sealed class LlmJudgeService : ILlmJudgeService
{
    private readonly IChatClient _chatClient;
    private readonly LlmJudgeOptions _options;

    public LlmJudgeService(IChatClient chatClient, IOptions<LlmJudgeOptions> options)
    {
        _chatClient = chatClient;
        _options = options.Value;
    }

    public async Task<JudgeResult> JudgeCorrectnessAsync(
        string question,
        string expectedAnswer,
        string generatedAnswer,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            You are an expert evaluator. Compare the generated answer to the expected answer for the given question.

            Question: {{question}}

            Expected Answer: {{expectedAnswer}}

            Generated Answer: {{generatedAnswer}}

            Score the correctness on a scale of 0.0 to 1.0:
            - 1.0: Perfect match, all key information present and accurate
            - 0.8-0.9: Minor omissions or slight inaccuracies
            - 0.5-0.7: Partially correct, missing significant information
            - 0.2-0.4: Mostly incorrect but has some relevant information
            - 0.0-0.1: Completely incorrect or irrelevant

            Respond with JSON only:
            {"score": 0.0, "reasoning": "explanation", "passed": true/false}
            """;

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return ParseJudgeResponse(response.Text ?? "{}");
    }

    public async Task<JudgeResult> JudgeHallucinationAsync(
        string question,
        string generatedAnswer,
        IReadOnlyList<string> contexts,
        CancellationToken cancellationToken = default)
    {
        var contextText = contexts.Count > 0
            ? string.Join("\n\n---\n\n", contexts)
            : "No context provided.";

        var prompt = $$"""
            You are an expert evaluator. Determine if the generated answer contains hallucinations (information not supported by the provided context).

            Question: {{question}}

            Context:
            {{contextText}}

            Generated Answer: {{generatedAnswer}}

            Score the hallucination level on a scale of 0.0 to 1.0:
            - 0.0: No hallucination, all claims fully supported by context
            - 0.1-0.3: Minor unsupported claims or slight extrapolations
            - 0.4-0.6: Moderate hallucination, several unsupported claims
            - 0.7-0.9: Major hallucination, most claims unsupported
            - 1.0: Complete hallucination, nothing supported by context

            Respond with JSON only:
            {"score": 0.0, "reasoning": "explanation", "passed": true/false}
            """;

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return ParseJudgeResponse(response.Text ?? "{}");
    }

    public async Task<JudgeResult> JudgeCompletenessAsync(
        string question,
        string generatedAnswer,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            You are an expert evaluator. Determine if the generated answer fully addresses the question.

            Question: {{question}}

            Generated Answer: {{generatedAnswer}}

            Score the completeness on a scale of 0.0 to 1.0:
            - 1.0: Fully addresses all parts of the question
            - 0.8-0.9: Addresses most parts, minor omissions
            - 0.5-0.7: Addresses some parts, significant omissions
            - 0.2-0.4: Addresses few parts, mostly incomplete
            - 0.0-0.1: Does not address the question

            Respond with JSON only:
            {"score": 0.0, "reasoning": "explanation", "passed": true/false}
            """;

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return ParseJudgeResponse(response.Text ?? "{}");
    }

    public async Task<JudgeResult> JudgeToneAsync(
        string question,
        string generatedAnswer,
        CancellationToken cancellationToken = default)
    {
        var prompt = $$"""
            You are an expert evaluator. Determine if the generated answer has an appropriate tone for a student management application help assistant.

            Question: {{question}}

            Generated Answer: {{generatedAnswer}}

            Score the tone appropriateness on a scale of 0.0 to 1.0:
            - 1.0: Professional, helpful, concise, well-formatted
            - 0.8-0.9: Good tone, minor issues
            - 0.5-0.7: Acceptable but could be more professional/helpful
            - 0.2-0.4: Poor tone, unhelpful, or poorly formatted
            - 0.0-0.1: Inappropriate, rude, or completely unhelpful

            Respond with JSON only:
            {"score": 0.0, "reasoning": "explanation", "passed": true/false}
            """;

        var response = await _chatClient.GetResponseAsync(prompt, cancellationToken: cancellationToken);
        return ParseJudgeResponse(response.Text ?? "{}");
    }

    private static JudgeResult ParseJudgeResponse(string response)
    {
        try
        {
            var json = System.Text.Json.JsonDocument.Parse(response).RootElement;
            var score = json.TryGetProperty("score", out var s) ? s.GetDouble() : 0.0;
            var reasoning = json.TryGetProperty("reasoning", out var r) ? r.GetString() ?? "" : "";
            var passed = json.TryGetProperty("passed", out var p) ? p.GetBoolean() : score >= 0.7;
            return new JudgeResult(Math.Clamp(score, 0.0, 1.0), reasoning, passed);
        }
        catch
        {
            return JudgeResult.Fail("Failed to parse judge response");
        }
    }
}

public sealed class LlmJudgeOptions
{
    public string Model { get; set; } = "qwen2.5:3b-instruct";
    public double Temperature { get; set; } = 0.0;
    public int MaxTokens { get; set; } = 500;
}