using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;

namespace StudentManagement.Application.Services
{
    public class AppKnowledgeService : IAppKnowledgeService
    {
        private const string DocumentationCacheKey = "app_knowledge_documentation";

        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
        private readonly int _maxRagChunks;
        private readonly IVectorStore _vectorStore;
        private readonly IEnumerable<IDocumentParser> _parsers;
        private readonly SemanticChunker _semanticChunker;
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<AppKnowledgeService> _logger;
        private readonly Lazy<Task> _initialization;

        private IReadOnlyList<KnowledgeChunk> _chunks = [];
        private bool _chunksLoaded;

        public AppKnowledgeService(
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            IOptions<ChatFeatureOptions> features,
            IVectorStore vectorStore,
            IEnumerable<IDocumentParser> parsers,
            SemanticChunker semanticChunker,
            IMemoryCache memoryCache,
            ILogger<AppKnowledgeService> logger)
        {
            _embeddingGenerator = embeddingGenerator;
            _maxRagChunks = features.Value.MaxRagChunks;
            _vectorStore = vectorStore;
            _parsers = parsers;
            _semanticChunker = semanticChunker;
            _memoryCache = memoryCache;
            _logger = logger;
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

            var embeddings = await GenerateEmbeddingsAsync(_chunks);
            var documents = _chunks
                .Select((chunk, index) => new VectorDocument(
                    Guid.NewGuid().ToString(),
                    chunk.Title,
                    chunk.Content,
                    embeddings[index]))
                .ToList();

            await _vectorStore.ReplaceAsync(documents);
        }

        public string GetDocumentation()
        {
            LoadChunks();

            if (_memoryCache.TryGetValue(DocumentationCacheKey, out string? cached) && cached is not null)
            {
                _logger.LogInformation("Cache HIT {CacheKey} (in-process)", DocumentationCacheKey);
                return cached;
            }

            _logger.LogInformation("Cache MISS {CacheKey} (in-process)", DocumentationCacheKey);
            var documentation = string.Join(
                Environment.NewLine + Environment.NewLine,
                _chunks.Select(chunk => chunk.Content));

            // The embedded knowledge base is immutable for the lifetime of the process, so the joined
            // documentation is cached without expiry.
            _memoryCache.Set(DocumentationCacheKey, documentation, new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.NeverRemove
            });

            return documentation;
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

            var results = await _vectorStore.QueryAsync(new ReadOnlyMemory<float>(queryEmbedding), maxChunks);

            var formatted = results
                .Where(r => r.Distance < 0.7f)
                .Select(r => $"### {r.Title}\n{r.Content}")
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
