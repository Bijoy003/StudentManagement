using Microsoft.Extensions.AI;
using StudentManagement.Application.Evaluation.Models;
using StudentManagement.Application.Evaluation.Interfaces;

namespace StudentManagement.Application.Evaluation.Services;

public sealed class DeterministicMetricsCalculator : IDeterministicMetricsCalculator
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public DeterministicMetricsCalculator(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task<EvaluationMetrics> CalculateAsync(
        EvaluationQuestion question,
        string generatedAnswer,
        IReadOnlyList<string> retrievedContexts,
        CancellationToken cancellationToken = default)
    {
        var relevanceTask = CalculateRelevanceAsync(question.Question, generatedAnswer, cancellationToken);
        var groundednessTask = Task.FromResult(CalculateGroundedness(generatedAnswer, retrievedContexts));
        var correctnessTask = CalculateCorrectnessAsync(question.ExpectedAnswer, generatedAnswer, cancellationToken);
        var hallucinationTask = CalculateHallucinationAsync(generatedAnswer, retrievedContexts, cancellationToken);

        await Task.WhenAll(relevanceTask, groundednessTask, correctnessTask, hallucinationTask);

        return new EvaluationMetrics(
            Relevance: relevanceTask.Result,
            Groundedness: groundednessTask.Result,
            Correctness: correctnessTask.Result,
            Hallucination: hallucinationTask.Result);
    }

    private async Task<double> CalculateRelevanceAsync(
        string question,
        string answer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(answer))
            return 0.0;

        try
        {
            var questionEmbedding = await _embeddingGenerator.GenerateAsync([question], cancellationToken: cancellationToken);
            var answerEmbedding = await _embeddingGenerator.GenerateAsync([answer], cancellationToken: cancellationToken);

            return CosineSimilarity(questionEmbedding[0].Vector.ToArray(), answerEmbedding[0].Vector.ToArray());
        }
        catch
        {
            return 0.0;
        }
    }

    private static double CalculateGroundedness(
        string answer,
        IReadOnlyList<string> contexts)
    {
        if (string.IsNullOrWhiteSpace(answer) || contexts.Count == 0)
            return 0.0;

        var answerTokens = Tokenize(answer.ToLowerInvariant());
        if (answerTokens.Count == 0)
            return 0.0;

        var contextText = string.Join(" ", contexts).ToLowerInvariant();
        var contextTokens = Tokenize(contextText).ToHashSet();

        var overlap = answerTokens.Count(t => contextTokens.Contains(t));
        return (double)overlap / answerTokens.Count;
    }

    private async Task<double> CalculateCorrectnessAsync(
        string expectedAnswer,
        string generatedAnswer,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(expectedAnswer) || string.IsNullOrWhiteSpace(generatedAnswer))
            return 0.0;

        try
        {
            var expectedEmbedding = await _embeddingGenerator.GenerateAsync([expectedAnswer], cancellationToken: cancellationToken);
            var generatedEmbedding = await _embeddingGenerator.GenerateAsync([generatedAnswer], cancellationToken: cancellationToken);

            return CosineSimilarity(expectedEmbedding[0].Vector.ToArray(), generatedEmbedding[0].Vector.ToArray());
        }
        catch
        {
            return 0.0;
        }
    }

    private async Task<double> CalculateHallucinationAsync(
        string answer,
        IReadOnlyList<string> contexts,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(answer) || contexts.Count == 0)
            return 1.0;

        try
        {
            var answerEmbedding = await _embeddingGenerator.GenerateAsync([answer], cancellationToken: cancellationToken);
            var maxSimilarity = 0.0;

            foreach (var context in contexts)
            {
                if (string.IsNullOrWhiteSpace(context))
                    continue;

                var contextEmbedding = await _embeddingGenerator.GenerateAsync([context], cancellationToken: cancellationToken);
                var similarity = CosineSimilarity(answerEmbedding[0].Vector.ToArray(), contextEmbedding[0].Vector.ToArray());
                maxSimilarity = Math.Max(maxSimilarity, similarity);
            }

            return 1.0 - maxSimilarity;
        }
        catch
        {
            return 1.0;
        }
    }

    private static double CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0.0;

        double dotProduct = 0;
        double normA = 0;
        double normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denominator = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denominator == 0 ? 0.0 : dotProduct / denominator;
    }

    private static List<string> Tokenize(string text)
    {
        return text
            .Split([' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\''], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)
            .ToList();
    }
}

public interface IDeterministicMetricsCalculator
{
    Task<EvaluationMetrics> CalculateAsync(
        EvaluationQuestion question,
        string generatedAnswer,
        IReadOnlyList<string> retrievedContexts,
        CancellationToken cancellationToken = default);
}