using ChromaDB.Client;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;

namespace StudentManagement.Application.Services
{
    public class AppKnowledgeService : IAppKnowledgeService
    {
        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
        private readonly int _maxRagChunks;
        private readonly ChromaClient _chromaClient;
        private readonly ChromaConfigurationOptions _chromaConfig;
        private readonly ChromaOptions _chromaOptions;
        private readonly HttpClient _httpClient;
        private readonly IEnumerable<IDocumentParser> _parsers;
        private readonly SemanticChunker _semanticChunker;
        private readonly Lazy<Task> _initialization;

        private IReadOnlyList<KnowledgeChunk> _chunks = [];
        private ChromaCollectionClient? _collectionClient;
        private bool _chunksLoaded;

        public AppKnowledgeService(
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            IOptions<ChatFeatureOptions> features,
            ChromaClient chromaClient,
            ChromaConfigurationOptions chromaConfig,
            IOptions<ChromaOptions> chromaOptions,
            HttpClient httpClient,
            IEnumerable<IDocumentParser> parsers,
            SemanticChunker semanticChunker)
        {
            _embeddingGenerator = embeddingGenerator;
            _maxRagChunks = features.Value.MaxRagChunks;
            _chromaClient = chromaClient;
            _chromaConfig = chromaConfig;
            _chromaOptions = chromaOptions.Value;
            _httpClient = httpClient;
            _parsers = parsers;
            _semanticChunker = semanticChunker;
            _initialization = new Lazy<Task>(InitializeCoreAsync);
        }

        public async Task InitializeAsync()
        {
            LoadChunks();
            await _initialization.Value;
        }

        private void LoadChunks()
        {
            if (_chunksLoaded) return;
            var rawDocs = LoadRawDocuments();
            _chunks = rawDocs.SelectMany(d => d.Chunks).ToList();
            _chunksLoaded = true;
        }

        private async Task InitializeCoreAsync()
        {
            LoadChunks();

            var metadata = new Dictionary<string, object> { { "hnsw:space", "cosine" } };
            var collection = await _chromaClient.GetOrCreateCollection(_chromaOptions.CollectionName, metadata);
            _collectionClient = new ChromaCollectionClient(collection, _chromaConfig, _httpClient);

            var existing = await _collectionClient.Get();
            if (existing.Count > 0)
            {
                await _collectionClient.Delete(existing.Select(r => r.Id).ToList());
            }

            var embeddings = await GenerateEmbeddingsAsync(_chunks);
            var ids = _chunks.Select(_ => Guid.NewGuid().ToString()).ToList();
            var documents = _chunks.Select(c => c.Content).ToList();
            var metadatas = _chunks.Select(c => new Dictionary<string, object> { { "title", c.Title } }).ToList();
            var readonlyEmbs = embeddings.Select(e => new ReadOnlyMemory<float>(e)).ToList();

            await _collectionClient.Add(ids,
                embeddings: readonlyEmbs,
                documents: documents,
                metadatas: metadatas);
        }

        public string GetDocumentation()
        {
            LoadChunks();
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                _chunks.Select(chunk => chunk.Content));
        }

        public async Task<string> SearchRelevantChunks(string query, int maxChunks = 3)
        {
            if (string.IsNullOrWhiteSpace(query) || maxChunks <= 0)
            {
                return string.Empty;
            }

            await InitializeAsync();

            var queryEmbedding = await GenerateEmbeddingAsync(query);
            if (queryEmbedding.Length == 0)
            {
                return string.Empty;
            }

            var results = await _collectionClient!.Query(
                new ReadOnlyMemory<float>(queryEmbedding),
                nResults: maxChunks,
                include: ChromaQueryInclude.Metadatas | ChromaQueryInclude.Documents | ChromaQueryInclude.Distances);

            var formatted = results
                .Where(r => r.Distance < 0.7f)
                .Select(r => $"### {r.Metadata!["title"]}\n{r.Document}")
                .ToList();

            return formatted.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine + Environment.NewLine, formatted);
        }

        private List<DocumentContent> LoadRawDocuments()
        {
            var assembly = typeof(AppKnowledgeService).Assembly;
            var resourceNames = assembly.GetManifestResourceNames()
                .Where(n => n.Contains(".AppKnowledge.", StringComparison.Ordinal))
                .ToList();

            var result = new List<DocumentContent>();
            foreach (var name in resourceNames)
            {
                var title = ExtractTitle(name);
                var extension = Path.GetExtension(name)?.ToLowerInvariant() ?? "";

                var parser = _parsers.FirstOrDefault(p => p.CanParse(name));
                if (parser == null)
                    continue;

                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null)
                    continue;

                var text = parser.ExtractTextAsync(stream).GetAwaiter().GetResult();
                var chunks = _semanticChunker.ChunkAsync(title, text).GetAwaiter().GetResult();
                result.Add(new DocumentContent(title, chunks));
            }

            return result;
        }

        private static string ExtractTitle(string resourceName)
        {
            var fileName = Path.GetFileNameWithoutExtension(resourceName);
            var parts = fileName.Split('.');
            return parts.Length > 1 ? parts[parts.Length - 1] : fileName;
        }

        private async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            var response = await _embeddingGenerator.GenerateAsync(text);
            return response.Vector.ToArray();
        }

        private async Task<float[][]> GenerateEmbeddingsAsync(IReadOnlyList<KnowledgeChunk> chunks)
        {
            var texts = chunks.Select(c => $"{c.Title}\n{c.Content}").ToList();
            var response = await _embeddingGenerator.GenerateAsync(texts);
            return response.Select(e => e.Vector.ToArray()).ToArray();
        }

        private sealed record DocumentContent(string Title, IReadOnlyList<KnowledgeChunk> Chunks);
    }
}
