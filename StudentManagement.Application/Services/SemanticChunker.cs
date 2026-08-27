using Microsoft.Extensions.AI;

namespace StudentManagement.Application.Services;

public class SemanticChunker
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;

    public SemanticChunker(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
    {
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> ChunkAsync(string documentTitle, string text)
    {
        var paragraphs = SplitParagraphs(text);
        if (paragraphs.Count == 0)
            return new List<KnowledgeChunk>();

        var topicShifts = await DetectTopicShifts(paragraphs);
        return MergeChunks(documentTitle, paragraphs, topicShifts);
    }

    private static List<string> SplitParagraphs(string text)
    {
        var paragraphs = new List<string>();
        var current = new System.Text.StringBuilder();

        using var reader = new System.IO.StringReader(text);
        string? line;
        bool lastWasBlank = false;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (!lastWasBlank && current.Length > 0)
                    current.Append(" ");
                lastWasBlank = true;
                continue;
            }
            if (lastWasBlank && current.Length > 0)
            {
                paragraphs.Add(current.ToString().Trim());
                current.Clear();
            }
            if (current.Length > 0) current.Append(" ");
            current.Append(line);
            lastWasBlank = false;
        }
        if (current.Length > 0)
            paragraphs.Add(current.ToString().Trim());

        return paragraphs;
    }

    private async Task<List<int>> DetectTopicShifts(List<string> paragraphs)
    {
        var shifts = new List<int>();
        var windowSize = Math.Min(3, paragraphs.Count);

        if (paragraphs.Count <= windowSize)
            return shifts;

        var windows = new List<string>();
        for (var i = 0; i <= paragraphs.Count - windowSize; i++)
        {
            windows.Add(string.Join(" ", paragraphs.Skip(i).Take(windowSize)));
        }

        var embeddings = await _embeddingGenerator.GenerateAsync(windows);
        var vectors = embeddings.Select(e => e.Vector.ToArray()).ToArray();

        for (var i = 1; i < vectors.Length; i++)
        {
            var sim = CosineSimilarity(vectors[i - 1], vectors[i]);
            if (sim < 0.55)
            {
                var splitIndex = i + windowSize - 1;
                if (!shifts.Contains(splitIndex))
                    shifts.Add(splitIndex);
            }
        }

        return shifts;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        magA = Math.Sqrt(magA);
        magB = Math.Sqrt(magB);
        return (float)(dot / (magA * magB));
    }

    private static List<KnowledgeChunk> MergeChunks(string documentTitle, List<string> paragraphs, List<int> shifts)
    {
        paragraphs = paragraphs.Select(p => p.Trim()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (paragraphs.Count == 0)
            return new List<KnowledgeChunk>();

        var result = new List<KnowledgeChunk>();
        var start = 0;

        foreach (var split in shifts.OrderBy(x => x).Where(x => x > start && x < paragraphs.Count))
        {
            var chunkText = string.Join("\n\n", paragraphs.GetRange(start, split - start));
            result.Add(new KnowledgeChunk(GetChunkTitle(chunkText, documentTitle), chunkText.Trim()));
            start = split;
        }

        if (start < paragraphs.Count)
        {
            var chunkText = string.Join("\n\n", paragraphs.GetRange(start, paragraphs.Count - start));
            result.Add(new KnowledgeChunk(GetChunkTitle(chunkText, documentTitle), chunkText));
        }

        return result;
    }

    private static string GetChunkTitle(string chunkText, string defaultTitle)
    {
        var firstSentence = chunkText.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        return !string.IsNullOrWhiteSpace(firstSentence) && firstSentence.Length < 100
            ? firstSentence
            : defaultTitle;
    }
}
