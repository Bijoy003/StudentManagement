namespace StudentManagement.Application.Interfaces
{
    public interface IVectorStore
    {
        Task ReplaceAsync(IReadOnlyList<VectorDocument> documents, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<VectorSearchResult>> QueryAsync(
            ReadOnlyMemory<float> embedding,
            int maxResults,
            CancellationToken cancellationToken = default);
    }

    public sealed record VectorDocument(
        string Id,
        string Title,
        string Content,
        ReadOnlyMemory<float> Embedding);

    public sealed record VectorSearchResult(
        string Title,
        string Content,
        float Distance);
}
