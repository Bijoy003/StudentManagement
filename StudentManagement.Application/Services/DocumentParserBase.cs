namespace StudentManagement.Application.Services;

// Template Method base for document parsers. The public IDocumentParser contract
// (CanParse + ExtractTextAsync) is unchanged; ExtractTextAsync is the sealed template
// entry point orchestrating:
//   1. ExtractText(stream) — abstract, format-specific, invoked exactly once
//   2. Normalize(text)     — shared default: trim + CRLF -> LF
//   3. Chunk(text)         — shared default: one KnowledgeChunk (empty Title, full text)
//   4. returns the normalized text from step 2
// Subclasses may override Normalize/Chunk; the template invokes the overrides.
public abstract class DocumentParserBase : IDocumentParser
{
    public abstract bool CanParse(string fileName);

    public Task<string> ExtractTextAsync(Stream stream)
    {
        var text = Normalize(ExtractText(stream));
        Chunk(text);
        return Task.FromResult(text);
    }

    protected abstract string ExtractText(Stream stream);

    protected virtual string Normalize(string text) =>
        text.Trim().Replace("\r\n", "\n");

    protected virtual IReadOnlyList<KnowledgeChunk> Chunk(string text) =>
        new List<KnowledgeChunk> { new(string.Empty, text) };
}
