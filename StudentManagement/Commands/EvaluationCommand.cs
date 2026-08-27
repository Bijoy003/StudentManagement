using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StudentManagement.Application.Evaluation;
using StudentManagement.Application.Evaluation.Datasets;
using StudentManagement.Application.Evaluation.Interfaces;
using StudentManagement.Application.Evaluation.Models;
using System.Text.Json;

namespace StudentManagement.Commands;

public static class EvaluationCommand
{
    public static async Task<int> RunAsync(string[] args, IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var scopedServices = scope.ServiceProvider;
        
        var logger = scopedServices.GetRequiredService<ILoggerFactory>().CreateLogger("EvaluationCommand");
        var evaluationService = scopedServices.GetRequiredService<IEvaluationService>();
        var datasetProvider = scopedServices.GetRequiredService<IEvaluationDatasetProvider>();
        var promptManager = scopedServices.GetRequiredService<IPromptManager>();

        var config = new EvaluationConfig();

        // Parse args
        string promptVersion = "v1";
        string? category = null;
        string? outputPath = null;
        bool compare = false;
        string? baselineVersion = null;
        string? variantVersion = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--prompt":
                case "-p":
                    if (i + 1 < args.Length) promptVersion = args[++i];
                    break;
                case "--category":
                case "-c":
                    if (i + 1 < args.Length) category = args[++i];
                    break;
                case "--output":
                case "-o":
                    if (i + 1 < args.Length) outputPath = args[++i];
                    break;
                case "--compare":
                    compare = true;
                    break;
                case "--baseline":
                    if (i + 1 < args.Length) baselineVersion = args[++i];
                    break;
                case "--variant":
                    if (i + 1 < args.Length) variantVersion = args[++i];
                    break;
                case "--list-prompts":
                    await ListPromptsAsync(promptManager, logger);
                    return 0;
                case "--help":
                case "-h":
                    PrintHelp();
                    return 0;
            }
        }

        try
        {
            var dataset = category != null
                ? await datasetProvider.GetDatasetByCategoryAsync(category)
                : await datasetProvider.GetDatasetAsync();

            logger.LogInformation("Loaded dataset: {Name} v{Version} ({Count} questions)", dataset.Name, dataset.Version, dataset.Questions.Count);

            if (compare)
            {
                if (string.IsNullOrEmpty(baselineVersion) || string.IsNullOrEmpty(variantVersion))
                {
                    logger.LogError("--compare requires --baseline and --variant");
                    return 1;
                }

                var experiment = PromptExperiment.Create(
                    "CLI Comparison",
                    $"Compare {baselineVersion} vs {variantVersion}",
                    baselineVersion,
                    variantVersion,
                    config);

                var result = await evaluationService.RunExperimentAsync(experiment, dataset);
                
                PrintExperimentResult(result, logger);
                
                if (!string.IsNullOrEmpty(outputPath))
                {
                    await SaveReportAsync(result, outputPath);
                    logger.LogInformation("Report saved to {Path}", outputPath);
                }

                return result.Comparison.IsSignificant && 
                       (result.Comparison.RelevanceDelta < 0 || result.Comparison.GroundednessDelta < 0 || result.Comparison.CorrectnessDelta < 0 || result.Comparison.HallucinationDelta > 0)
                    ? 1 : 0;
            }
            else
            {
                var run = await evaluationService.RunEvaluationAsync(dataset, config, promptVersion);
                
                PrintRunSummary(run, logger);
                
                if (!string.IsNullOrEmpty(outputPath))
                {
                    await SaveReportAsync(run, outputPath);
                    logger.LogInformation("Report saved to {Path}", outputPath);
                }

                return run.GetSummary().PassedCount == run.GetSummary().TotalQuestions ? 0 : 1;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Evaluation failed");
            return 1;
        }
    }

    private static async Task ListPromptsAsync(IPromptManager promptManager, ILogger logger)
    {
        var prompts = await promptManager.GetAllPromptsAsync();
        logger.LogInformation("Available prompt versions:");
        foreach (var p in prompts)
        {
            logger.LogInformation("  {Version}: {Name} - {Description}", p.Version, p.Name, p.Description);
        }
    }

    private static void PrintRunSummary(EvaluationRun run, ILogger logger)
    {
        var summary = run.GetSummary();
        logger.LogInformation("=== Evaluation Run {RunId} ===", run.RunId);
        logger.LogInformation("Prompt Version: {Version}", run.PromptVersion);
        logger.LogInformation("Total Questions: {Total}", summary.TotalQuestions);
        logger.LogInformation("Passed: {Passed} ({PassRate:P1})", summary.PassedCount, summary.PassRate);
        logger.LogInformation("Avg Relevance: {Relevance:F3}", summary.AvgRelevance);
        logger.LogInformation("Avg Groundedness: {Groundedness:F3}", summary.AvgGroundedness);
        logger.LogInformation("Avg Correctness: {Correctness:F3}", summary.AvgCorrectness);
        logger.LogInformation("Avg Hallucination: {Hallucination:F3}", summary.AvgHallucination);
        logger.LogInformation("Avg Latency: {Latency:F0}ms", summary.AvgLatencyMs);

        logger.LogInformation("\nPer-Question Results:");
        foreach (var result in run.Results)
        {
            var status = result.Metrics.PassesThresholds() ? "PASS" : "FAIL";
            logger.LogInformation("  [{Status}] {Question} (Rel:{Rel:F2} Grd:{Grd:F2} Cor:{Cor:F2} Hal:{Hal:F2})",
                status, result.Question[..Math.Min(60, result.Question.Length)],
                result.Metrics.Relevance, result.Metrics.Groundedness, result.Metrics.Correctness, result.Metrics.Hallucination);
        }
    }

    private static void PrintExperimentResult(ExperimentResult result, ILogger logger)
    {
        logger.LogInformation("=== Experiment {ExperimentId} ===", result.ExperimentId);
        logger.LogInformation("Baseline: {Baseline} | Variant: {Variant}", result.BaselineRun.PromptVersion, result.VariantRun.PromptVersion);
        
        var comp = result.Comparison;
        logger.LogInformation("Relevance Delta: {Delta:+0.000;-0.000;0.000} ({Sig})", comp.RelevanceDelta, comp.IsSignificant ? "SIG" : "ns");
        logger.LogInformation("Groundedness Delta: {Delta:+0.000;-0.000;0.000}", comp.GroundednessDelta);
        logger.LogInformation("Correctness Delta: {Delta:+0.000;-0.000;0.000}", comp.CorrectnessDelta);
        logger.LogInformation("Hallucination Delta: {Delta:+0.000;-0.000;0.000}", comp.HallucinationDelta);
        logger.LogInformation("P-Value: {PValue:F4} | Significant: {Sig}", comp.PValue, comp.IsSignificant);
        logger.LogInformation("Sample Size: {Size}", comp.SampleSize);

        logger.LogInformation("\nBaseline Summary:");
        PrintRunSummary(result.BaselineRun, logger);
        
        logger.LogInformation("\nVariant Summary:");
        PrintRunSummary(result.VariantRun, logger);
    }

    private static async Task SaveReportAsync(object report, string path)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(report, options);
        await File.WriteAllTextAsync(path, json);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            Evaluation CLI
            
            Usage: dotnet run -- evaluate [options]
            
            Options:
              -p, --prompt <version>      Prompt version to use (default: v1)
              -c, --category <name>       Filter by category
              -o, --output <path>         Save JSON report to file
              --compare                   Run A/B comparison
              --baseline <version>        Baseline prompt version (for --compare)
              --variant <version>         Variant prompt version (for --compare)
              --list-prompts              List available prompt versions
              -h, --help                  Show this help
            
            Examples:
              dotnet run -- evaluate
              dotnet run -- evaluate -p v2 -o report.json
              dotnet run -- evaluate -c "Student Management"
              dotnet run -- evaluate --compare --baseline v1 --variant v2 -o comparison.json
              dotnet run -- evaluate --list-prompts
            """);
    }
}