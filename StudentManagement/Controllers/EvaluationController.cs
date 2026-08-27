using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StudentManagement.Application.Evaluation;
using StudentManagement.Application.Evaluation.Datasets;
using StudentManagement.Application.Evaluation.Interfaces;
using StudentManagement.Application.Evaluation.Models;

namespace StudentManagement.Controllers;

[ApiController]
[Route("api/evaluation")]
[Authorize(Roles = "Admin")]
public sealed class EvaluationController : ControllerBase
{
    private readonly IEvaluationService _evaluationService;
    private readonly IEvaluationDatasetProvider _datasetProvider;
    private readonly IPromptManager _promptManager;
    private readonly ILogger<EvaluationController> _logger;

    public EvaluationController(
        IEvaluationService evaluationService,
        IEvaluationDatasetProvider datasetProvider,
        IPromptManager promptManager,
        ILogger<EvaluationController> logger)
    {
        _evaluationService = evaluationService;
        _datasetProvider = datasetProvider;
        _promptManager = promptManager;
        _logger = logger;
    }

    [HttpPost("run")]
    public async Task<ActionResult<EvaluationRun>> RunEvaluation([FromBody] RunEvaluationRequest request, CancellationToken ct)
    {
        try
        {
            var dataset = string.IsNullOrEmpty(request.Category)
                ? await _datasetProvider.GetDatasetAsync(ct)
                : await _datasetProvider.GetDatasetByCategoryAsync(request.Category, ct);

            var config = request.Config ?? new EvaluationConfig();
            var promptVersion = request.PromptVersion ?? "v1";

            _logger.LogInformation("Starting evaluation run via API: prompt={Prompt}, questions={Count}", promptVersion, dataset.Questions.Count);

            var run = await _evaluationService.RunEvaluationAsync(dataset, config, promptVersion, ct);
            
            return Ok(run);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Evaluation run failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("compare")]
    public async Task<ActionResult<ExperimentResult>> ComparePrompts([FromBody] ComparePromptsRequest request, CancellationToken ct)
    {
        try
        {
            var dataset = string.IsNullOrEmpty(request.Category)
                ? await _datasetProvider.GetDatasetAsync(ct)
                : await _datasetProvider.GetDatasetByCategoryAsync(request.Category, ct);

            if (request.QuestionIds?.Count > 0)
            {
                dataset = dataset.FilterByIds(request.QuestionIds);
            }

            var config = request.Config ?? new EvaluationConfig();
            
            var experiment = PromptExperiment.Create(
                "API Comparison",
                $"Compare {request.BaselinePromptVersion} vs {request.VariantPromptVersion}",
                request.BaselinePromptVersion,
                request.VariantPromptVersion,
                config,
                request.QuestionIds?.ToArray() ?? []);

            _logger.LogInformation("Starting prompt comparison via API: {Baseline} vs {Variant}", request.BaselinePromptVersion, request.VariantPromptVersion);

            var result = await _evaluationService.RunExperimentAsync(experiment, dataset, ct);
            
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Prompt comparison failed");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("runs")]
    public async Task<ActionResult<IReadOnlyList<EvaluationRun>>> GetRuns([FromQuery] int maxRuns = 50, CancellationToken ct = default)
    {
        var runs = await _evaluationService.GetRunHistoryAsync(maxRuns, ct);
        return Ok(runs);
    }

    [HttpGet("runs/{runId}")]
    public async Task<ActionResult<EvaluationRun>> GetRun(string runId, CancellationToken ct)
    {
        var run = await _evaluationService.GetRunAsync(runId, ct);
        return run != null ? Ok(run) : NotFound();
    }

    [HttpGet("prompts")]
    public async Task<ActionResult<IReadOnlyList<PromptTemplate>>> GetPrompts(CancellationToken ct)
    {
        var prompts = await _promptManager.GetAllPromptsAsync(ct);
        return Ok(prompts);
    }

    [HttpGet("prompts/current")]
    public async Task<ActionResult<PromptTemplate>> GetCurrentPrompt(CancellationToken ct)
    {
        var prompt = await _promptManager.GetCurrentPromptAsync(ct);
        return Ok(prompt);
    }

    [HttpPost("prompts/current")]
    public async Task<ActionResult> SetCurrentPrompt([FromBody] SetPromptRequest request, CancellationToken ct)
    {
        try
        {
            await _promptManager.SetCurrentPromptAsync(request.Version, ct);
            return Ok(new { message = $"Current prompt set to {request.Version}" });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = $"Prompt version '{request.Version}' not found" });
        }
    }

    [HttpGet("dataset")]
    public async Task<ActionResult<EvaluationDataset>> GetDataset([FromQuery] string? category, CancellationToken ct = default)
    {
        var dataset = string.IsNullOrEmpty(category)
            ? await _datasetProvider.GetDatasetAsync(ct)
            : await _datasetProvider.GetDatasetByCategoryAsync(category, ct);
        return Ok(dataset);
    }

    [HttpGet("dataset/stats")]
    public async Task<ActionResult<Dictionary<string, object>>> GetDatasetStats(CancellationToken ct)
    {
        var dataset = await _datasetProvider.GetDatasetAsync(ct);
        return Ok(dataset.GetStats());
    }
}

public sealed record RunEvaluationRequest(
    string? PromptVersion,
    string? Category,
    EvaluationConfig? Config);

public sealed record ComparePromptsRequest(
    string BaselinePromptVersion,
    string VariantPromptVersion,
    string? Category,
    EvaluationConfig? Config,
    IReadOnlyList<string>? QuestionIds);

public sealed record SetPromptRequest(string Version);