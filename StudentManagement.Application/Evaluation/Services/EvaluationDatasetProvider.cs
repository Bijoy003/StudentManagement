using StudentManagement.Application.Evaluation.Datasets;
using StudentManagement.Application.Evaluation.Interfaces;
using StudentManagement.Application.Evaluation.Models;

namespace StudentManagement.Application.Evaluation.Services;

public sealed class EvaluationDatasetProvider : IEvaluationDatasetProvider
{
    private readonly EvaluationDataset _defaultDataset;

    public EvaluationDatasetProvider()
    {
        _defaultDataset = StudentManagementEvaluationDataset.GetDefault();
    }

    public Task<EvaluationDataset> GetDatasetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_defaultDataset);
    }

    public Task<EvaluationDataset> GetDatasetByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_defaultDataset.FilterByCategory(category));
    }
}