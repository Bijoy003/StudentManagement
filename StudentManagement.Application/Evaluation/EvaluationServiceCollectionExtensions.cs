using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Evaluation.Configuration;
using StudentManagement.Application.Evaluation.Datasets;
using StudentManagement.Application.Evaluation.Interfaces;
using StudentManagement.Application.Evaluation.Services;

namespace StudentManagement.Application.Evaluation;

public static class EvaluationServiceCollectionExtensions
{
    public static IServiceCollection AddEvaluationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EvaluationOptions>(configuration.GetSection(EvaluationOptions.SectionName));

        services.AddSingleton<IEvaluationDatasetProvider, EvaluationDatasetProvider>();
        services.AddSingleton<IDeterministicMetricsCalculator, DeterministicMetricsCalculator>();
        services.AddSingleton<ILlmJudgeService, LlmJudgeService>();
        services.AddSingleton<IPromptManager, PromptManager>();
        services.AddScoped<IEvaluationService, EvaluationService>();

        return services;
    }
}