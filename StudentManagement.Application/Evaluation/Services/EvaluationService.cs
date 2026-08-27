using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Evaluation.Configuration;
using StudentManagement.Application.Evaluation.Datasets;
using StudentManagement.Application.Evaluation.Interfaces;
using StudentManagement.Application.Evaluation.Models;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;

namespace StudentManagement.Application.Evaluation.Services;

public sealed class EvaluationService : IEvaluationService
{
    private readonly IChatClient _chatClient;
    private readonly IAppKnowledgeService _appKnowledge;
    private readonly ChatTools _chatTools;
    private readonly IMcpToolProvider _mcpToolProvider;
    private readonly IOptions<ChatFeatureOptions> _features;
    private readonly IOptions<McpOptions> _mcpOptions;
    private readonly IOptions<EvaluationOptions> _evalOptions;
    private readonly IDeterministicMetricsCalculator _deterministicCalculator;
    private readonly ILlmJudgeService _llmJudge;
    private readonly IPromptManager _promptManager;
    private readonly ILogger<EvaluationService> _logger;
    private readonly List<EvaluationRun> _runHistory = [];

    public EvaluationService(
        IChatClient chatClient,
        IAppKnowledgeService appKnowledge,
        ChatTools chatTools,
        IMcpToolProvider mcpToolProvider,
        IOptions<ChatFeatureOptions> features,
        IOptions<McpOptions> mcpOptions,
        IOptions<EvaluationOptions> evalOptions,
        IDeterministicMetricsCalculator deterministicCalculator,
        ILlmJudgeService llmJudge,
        IPromptManager promptManager,
        ILogger<EvaluationService> logger)
    {
        _chatClient = chatClient;
        _appKnowledge = appKnowledge;
        _chatTools = chatTools;
        _mcpToolProvider = mcpToolProvider;
        _features = features;
        _mcpOptions = mcpOptions;
        _evalOptions = evalOptions;
        _deterministicCalculator = deterministicCalculator;
        _llmJudge = llmJudge;
        _promptManager = promptManager;
        _logger = logger;
    }

    public async Task<EvaluationRun> RunEvaluationAsync(
        EvaluationDataset dataset,
        EvaluationConfig config,
        string promptVersion,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting evaluation run with prompt version {PromptVersion}, {QuestionCount} questions",
            promptVersion, dataset.Questions.Count);

        var promptTemplate = await _promptManager.GetPromptAsync(promptVersion, cancellationToken);
        var results = new List<EvaluationResult>();

        foreach (var question in dataset.Questions)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var result = await EvaluateSingleAsync(question, config, promptVersion, cancellationToken);
                results.Add(result);

                _logger.LogDebug("Evaluated question {QuestionId}: Relevance={Relevance:F2}, Groundedness={Groundedness:F2}, Correctness={Correctness:F2}, Hallucination={Hallucination:F2}",
                    question.Id, result.Metrics.Relevance, result.Metrics.Groundedness, result.Metrics.Correctness, result.Metrics.Hallucination);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to evaluate question {QuestionId}", question.Id);
                results.Add(new EvaluationResult(
                    question.Id,
                    question.Question,
                    $"ERROR: {ex.Message}",
                    [],
                    EvaluationMetrics.Empty,
                    promptVersion,
                    DateTimeOffset.UtcNow));
            }
        }

        var run = EvaluationRun.Create(promptVersion, config, results);
        _runHistory.Add(run);

        _logger.LogInformation("Evaluation run {RunId} completed. Pass rate: {PassRate:P1}, Avg Relevance: {Relevance:F2}, Avg Groundedness: {Groundedness:F2}",
            run.RunId, run.GetSummary().PassRate, run.GetSummary().AvgRelevance, run.GetSummary().AvgGroundedness);

        return run;
    }

    public async Task<EvaluationResult> EvaluateSingleAsync(
        EvaluationQuestion question,
        EvaluationConfig config,
        string promptVersion,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var ragContext = config.MaxRagChunks > 0
            ? await _appKnowledge.SearchRelevantChunks(question.Question, config.MaxRagChunks)
            : string.Empty;

        var retrievedContexts = string.IsNullOrWhiteSpace(ragContext)
            ? []
            : ragContext.Split(new[] { "\n\n### " }, StringSplitOptions.RemoveEmptyEntries).ToList();

        var promptTemplate = await _promptManager.GetPromptAsync(promptVersion, cancellationToken);
        var systemPrompt = BuildSystemPrompt(promptTemplate, ragContext, config);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, question.Question)
        };

        var tools = new List<AITool>();
        if (config.IncludeTools)
        {
            tools.AddRange(_chatTools.GetTools());
        }

        if (_mcpOptions.Value.Enabled)
        {
            tools.AddRange(await _mcpToolProvider.GetToolsAsync(cancellationToken));
        }

        var options = new ChatOptions();
        if (tools.Count > 0)
        {
            options.Tools = tools;
        }

        var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
        var generatedAnswer = response.Text ?? string.Empty;

        stopwatch.Stop();

        var deterministicMetrics = await _deterministicCalculator.CalculateAsync(
            question, generatedAnswer, retrievedContexts, cancellationToken);

        var judgeTasks = new List<Task<JudgeResult>>();
        if (_evalOptions.Value.UseLlmJudge)
        {
            judgeTasks.Add(_llmJudge.JudgeCorrectnessAsync(question.Question, question.ExpectedAnswer, generatedAnswer, cancellationToken));
            judgeTasks.Add(_llmJudge.JudgeHallucinationAsync(question.Question, generatedAnswer, retrievedContexts, cancellationToken));
            judgeTasks.Add(_llmJudge.JudgeCompletenessAsync(question.Question, generatedAnswer, cancellationToken));
            judgeTasks.Add(_llmJudge.JudgeToneAsync(question.Question, generatedAnswer, cancellationToken));
        }

        var judgeResults = await Task.WhenAll(judgeTasks);

        var finalMetrics = CombineMetrics(deterministicMetrics, judgeResults, config);

        return EvaluationResult.Create(question, generatedAnswer, retrievedContexts, finalMetrics, promptVersion);
    }

    public async Task<ExperimentResult> RunExperimentAsync(
        PromptExperiment experiment,
        EvaluationDataset dataset,
        CancellationToken cancellationToken = default)
    {
        var filteredDataset = experiment.QuestionIds.Count > 0
            ? dataset.FilterByIds(experiment.QuestionIds)
            : dataset;

        _logger.LogInformation("Running experiment {ExperimentId}: {Baseline} vs {Variant} on {Count} questions",
            experiment.Id, experiment.BaselinePromptVersion, experiment.VariantPromptVersion, filteredDataset.Questions.Count);

        var baselineRun = await RunEvaluationAsync(filteredDataset, experiment.Config, experiment.BaselinePromptVersion, cancellationToken);
        var variantRun = await RunEvaluationAsync(filteredDataset, experiment.Config, experiment.VariantPromptVersion, cancellationToken);

        var comparison = CompareRuns(baselineRun, variantRun);

        return new ExperimentResult(
            experiment.Id,
            baselineRun,
            variantRun,
            comparison,
            DateTimeOffset.UtcNow);
    }

    public Task<IReadOnlyList<EvaluationRun>> GetRunHistoryAsync(int maxRuns = 50, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<EvaluationRun>>(_runHistory.OrderByDescending(r => r.StartedAt).Take(maxRuns).ToList());
    }

    public Task<EvaluationRun?> GetRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_runHistory.FirstOrDefault(r => r.RunId == runId));
    }

    private string BuildSystemPrompt(PromptTemplate template, string ragContext, EvaluationConfig config)
    {
        var mcpInstructions = "";
        if (_mcpOptions.Value.Enabled)
        {
            mcpInstructions = """
                
                ## External integrations (MCP)
                - When the user asks about Jira issues, projects, or Atlassian data, use the jira_* tools.
                - When the user asks about GitHub repositories, issues, or pull requests, use the github_* tools.
                - Only use external integration tools when the question clearly requires that external system.
                - Do not invent issue keys, PR numbers, or repository data; always call the appropriate tool first.
                """;
        }

        var fullDocumentation = "";
        if (config.IncludeFullDocumentation)
        {
            var documentation = _appKnowledge.GetDocumentation();
            fullDocumentation = $"""

                
                ## Application documentation
                {documentation}
                """;
        }

        var ragContextSection = "";
        if (!string.IsNullOrWhiteSpace(ragContext))
        {
            ragContextSection = $"""

                
                ## Relevant documentation for this question
                {ragContext}
                """;
        }

        return template.Render(new Dictionary<string, string>
        {
            ["mcp_instructions"] = mcpInstructions,
            ["full_documentation"] = fullDocumentation,
            ["rag_context"] = ragContextSection
        });
    }

    private EvaluationMetrics CombineMetrics(
        EvaluationMetrics deterministic,
        JudgeResult[] judgeResults,
        EvaluationConfig config)
    {
        if (judgeResults.Length == 0)
            return deterministic;

        var correctnessJudge = judgeResults.Length > 0 ? judgeResults[0] : null;
        var hallucinationJudge = judgeResults.Length > 1 ? judgeResults[1] : null;
        var completenessJudge = judgeResults.Length > 2 ? judgeResults[2] : null;
        var toneJudge = judgeResults.Length > 3 ? judgeResults[3] : null;

        return new EvaluationMetrics(
            Relevance: deterministic.Relevance,
            Groundedness: deterministic.Groundedness,
            Correctness: correctnessJudge?.Score ?? deterministic.Correctness,
            Hallucination: hallucinationJudge?.Score ?? deterministic.Hallucination,
            Completeness: completenessJudge?.Score,
            ToneScore: toneJudge?.Score,
            LatencyMs: deterministic.LatencyMs);
    }

    private StatisticalComparison CompareRuns(EvaluationRun baseline, EvaluationRun variant)
    {
        var baselineResults = baseline.Results.ToDictionary(r => r.QuestionId);
        var variantResults = variant.Results.ToDictionary(r => r.QuestionId);

        var commonIds = baselineResults.Keys.Intersect(variantResults.Keys).ToList();
        var sampleSize = commonIds.Count;

        if (sampleSize == 0)
        {
            return new StatisticalComparison(0, 0, 0, 0, false, 1.0, 0, []);
        }

        var relevanceDeltas = new List<double>();
        var groundednessDeltas = new List<double>();
        var correctnessDeltas = new List<double>();
        var hallucinationDeltas = new List<double>();
        var perQuestionDeltas = new Dictionary<string, double>();

        foreach (var id in commonIds)
        {
            var b = baselineResults[id].Metrics;
            var v = variantResults[id].Metrics;

            var relDelta = v.Relevance - b.Relevance;
            var grdDelta = v.Groundedness - b.Groundedness;
            var corDelta = v.Correctness - b.Correctness;
            var halDelta = v.Hallucination - b.Hallucination;

            relevanceDeltas.Add(relDelta);
            groundednessDeltas.Add(grdDelta);
            correctnessDeltas.Add(corDelta);
            hallucinationDeltas.Add(halDelta);

            perQuestionDeltas[id] = (relDelta + grdDelta + corDelta - halDelta) / 4.0;
        }

        var avgRelevanceDelta = relevanceDeltas.Average();
        var avgGroundednessDelta = groundednessDeltas.Average();
        var avgCorrectnessDelta = correctnessDeltas.Average();
        var avgHallucinationDelta = hallucinationDeltas.Average();

        var pValue = CalculatePairedTTestPValue(relevanceDeltas);
        var isSignificant = pValue < 0.05;

        return new StatisticalComparison(
            avgRelevanceDelta,
            avgGroundednessDelta,
            avgCorrectnessDelta,
            avgHallucinationDelta,
            isSignificant,
            pValue,
            sampleSize,
            perQuestionDeltas);
    }

    private static double CalculatePairedTTestPValue(IReadOnlyList<double> differences)
    {
        if (differences.Count < 2)
            return 1.0;

        var mean = differences.Average();
        var variance = differences.Sum(d => Math.Pow(d - mean, 2)) / (differences.Count - 1);
        var stdError = Math.Sqrt(variance / differences.Count);

        if (stdError == 0)
            return mean == 0 ? 1.0 : 0.0;

        var tStat = mean / stdError;
        var df = differences.Count - 1;

        return 2.0 * (1.0 - StudentTDistribution(tStat, df));
    }

    private static double StudentTDistribution(double t, int df)
    {
        if (df <= 0)
            return 0.5;

        var x = df / (t * t + df);
        var beta = BetaFunction(0.5 * df, 0.5);
        var incompleteBeta = IncompleteBetaFunction(0.5 * df, 0.5, x);

        return 1.0 - 0.5 * incompleteBeta / beta;
    }

    private static double BetaFunction(double a, double b)
    {
        return Math.Exp(LogGamma(a) + LogGamma(b) - LogGamma(a + b));
    }

    private static double IncompleteBetaFunction(double a, double b, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return BetaFunction(a, b);

        var bt = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        var sum = 0.0;
        var term = 1.0;

        for (int i = 1; i <= 100; i++)
        {
            term *= (b - i) * x / (a + i);
            sum += term;
            if (Math.Abs(term) < 1e-10)
                break;
        }

        return bt * sum / a;
    }

    private static double LogGamma(double x)
    {
        var coeff = new double[] { 76.18009173, -86.50532033, 24.01409822, -1.231739516, 0.00120858003, -0.00000536382 };
        var xx = x - 1;
        var tmp = xx + 5.5;
        tmp -= (xx + 0.5) * Math.Log(tmp);
        var ser = 1.000000000190015;
        for (int i = 0; i < 6; i++)
        {
            ser += coeff[i] / ++xx;
        }
        return -tmp + Math.Log(2.5066282746310005 * ser / x);
    }
}