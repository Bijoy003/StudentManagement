using StudentManagement.Application.Interfaces;

namespace StudentManagement.Application.Services
{
    public class AppKnowledgeService : IAppKnowledgeService
    {
        private readonly Lazy<IReadOnlyList<KnowledgeChunk>> _chunks;

        public AppKnowledgeService()
        {
            _chunks = new Lazy<IReadOnlyList<KnowledgeChunk>>(LoadChunks);
        }

        public string GetDocumentation()
        {
            return string.Join(
                Environment.NewLine + Environment.NewLine,
                _chunks.Value.Select(chunk => chunk.Content));
        }

        public string SearchRelevantChunks(string query, int maxChunks = 3)
        {
            if (string.IsNullOrWhiteSpace(query) || maxChunks <= 0)
            {
                return string.Empty;
            }

            var terms = Tokenize(query);
            if (terms.Count == 0)
            {
                return string.Empty;
            }

            var ranked = _chunks.Value
                .Select(chunk => new { Chunk = chunk, Score = ScoreChunk(chunk, terms) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Take(maxChunks)
                .Select(x => $"### {x.Chunk.Title}\n{x.Chunk.Content}")
                .ToList();

            return ranked.Count == 0
                ? string.Empty
                : string.Join(Environment.NewLine + Environment.NewLine, ranked);
        }

        private static IReadOnlyList<KnowledgeChunk> LoadChunks()
        {
            var assembly = typeof(AppKnowledgeService).Assembly;
            var resourceNames = assembly
                .GetManifestResourceNames()
                .Where(name => name.Contains(".AppKnowledge.", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (resourceNames.Count == 0)
            {
                return [];
            }

            return resourceNames
                .Select(name =>
                {
                    using var stream = assembly.GetManifestResourceStream(name);
                    using var reader = new StreamReader(stream!);
                    var content = reader.ReadToEnd();
                    var title = Path.GetFileNameWithoutExtension(name.Split('.')[^2]);
                    return new KnowledgeChunk(title, content);
                })
                .ToList();
        }

        private static HashSet<string> Tokenize(string text)
        {
            return text
                .ToLowerInvariant()
                .Split([' ', '\n', '\r', '\t', ',', '.', '?', '!', ':', ';', '-'], StringSplitOptions.RemoveEmptyEntries)
                .Where(term => term.Length > 2)
                .ToHashSet();
        }

        private static int ScoreChunk(KnowledgeChunk chunk, HashSet<string> terms)
        {
            var searchable = $"{chunk.Title} {chunk.Content}".ToLowerInvariant();
            return terms.Count(term => searchable.Contains(term, StringComparison.Ordinal));
        }

        private sealed record KnowledgeChunk(string Title, string Content);
    }
}
