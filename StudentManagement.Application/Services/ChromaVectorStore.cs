using ChromaDB.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;

namespace StudentManagement.Application.Services
{
    // Thin adapter over ChromaDB.Client so AppKnowledgeService can be exercised with an
    // in-memory store in unit tests (and Application stops owning collection lifecycle).
    public sealed class ChromaVectorStore : IVectorStore
    {
        private readonly ChromaClient _chromaClient;
        private readonly ChromaConfigurationOptions _chromaConfig;
        private readonly ChromaOptions _options;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ChromaVectorStore> _logger;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        private ChromaCollectionClient? _collectionClient;

        public ChromaVectorStore(
            ChromaClient chromaClient,
            ChromaConfigurationOptions chromaConfig,
            IOptions<ChromaOptions> options,
            HttpClient httpClient,
            ILogger<ChromaVectorStore> logger)
        {
            _chromaClient = chromaClient;
            _chromaConfig = chromaConfig;
            _options = options.Value;
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task ReplaceAsync(
            IReadOnlyList<VectorDocument> documents,
            CancellationToken cancellationToken = default)
        {
            var client = await GetCollectionClientAsync(cancellationToken);

            var existing = await client.Get();
            if (existing.Count > 0)
            {
                await client.Delete(existing.Select(r => r.Id).ToList());
            }

            if (documents.Count == 0)
            {
                return;
            }

            await client.Add(
                documents.Select(d => d.Id).ToList(),
                embeddings: documents.Select(d => d.Embedding).ToList(),
                documents: documents.Select(d => d.Content).ToList(),
                metadatas: documents
                    .Select(d => new Dictionary<string, object> { { "title", d.Title } })
                    .ToList());

            _logger.LogInformation("Indexed {ChunkCount} knowledge chunks into Chroma collection '{Collection}'.", documents.Count, _options.CollectionName);
        }

        public async Task<IReadOnlyList<VectorSearchResult>> QueryAsync(
            ReadOnlyMemory<float> embedding,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            var client = await GetCollectionClientAsync(cancellationToken);

            var results = await client.Query(
                embedding,
                nResults: maxResults,
                include: ChromaQueryInclude.Metadatas | ChromaQueryInclude.Documents | ChromaQueryInclude.Distances);

            return results
                .Select(r => new VectorSearchResult(
                    r.Metadata is not null && r.Metadata.TryGetValue("title", out var title)
                        ? title?.ToString() ?? string.Empty
                        : string.Empty,
                    r.Document ?? string.Empty,
                    r.Distance))
                .ToList();
        }

        private async Task<ChromaCollectionClient> GetCollectionClientAsync(CancellationToken cancellationToken)
        {
            if (_collectionClient is not null)
            {
                return _collectionClient;
            }

            await _initLock.WaitAsync(cancellationToken);
            try
            {
                if (_collectionClient is null)
                {
                    var metadata = new Dictionary<string, object> { { "hnsw:space", "cosine" } };
                    var collection = await _chromaClient.GetOrCreateCollection(_options.CollectionName, metadata);
                    _collectionClient = new ChromaCollectionClient(collection, _chromaConfig, _httpClient);
                }

                return _collectionClient;
            }
            finally
            {
                _initLock.Release();
            }
        }
    }
}
