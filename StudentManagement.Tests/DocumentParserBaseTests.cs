using StudentManagement.Application.Services;
using StudentManagement.Domain.Entities;

namespace StudentManagement.Tests;

// WP8 AC6 — Template Method: DocumentParserBase orchestrates
// ExtractText (abstract, format-specific) -> Normalize (shared) -> Chunk (shared).
// IDocumentParser's public contract (CanParse/ExtractTextAsync) is unchanged, and the
// existing parser tests (AppKnowledgeServiceTests) must stay green untouched.
public class DocumentParserBaseTests
{
    private const string RawText = "  raw\r\ntext  ";

    // Test-only concrete subclass overriding every hook to record the flow.
    private sealed class RecordingParser : DocumentParserBase
    {
        public int ExtractTextCalls;
        public string? NormalizeInput;
        public string? ChunkInput;

        public override bool CanParse(string fileName) => true;

        protected override string ExtractText(Stream stream)
        {
            ExtractTextCalls++;
            return RawText;
        }

        protected override string Normalize(string text)
        {
            NormalizeInput = text;
            return text.Trim();
        }

        protected override IReadOnlyList<KnowledgeChunk> Chunk(string text)
        {
            ChunkInput = text;
            return new List<KnowledgeChunk> { new("chunk-title", text) };
        }
    }

    // Subclass that keeps the base's shared default hooks, exposed for testing.
    private sealed class DefaultHookParser : DocumentParserBase
    {
        public override bool CanParse(string fileName) => fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase);

        protected override string ExtractText(Stream stream) => new StreamReader(stream).ReadToEnd();

        public string RunNormalize(string text) => Normalize(text);

        public IReadOnlyList<KnowledgeChunk> RunChunk(string text) => Chunk(text);
    }

    private static Stream StreamOf(string _) => new MemoryStream(new byte[] { 1 });

    [Fact]
    public async Task ExtractTextAsync_TemplateParser_ReturnsExtractTextCalledOnce()
    {
        // Arrange
        var parser = new RecordingParser();

        // Act
        await parser.ExtractTextAsync(StreamOf("x"));

        // Assert — template drives exactly one ExtractText call
        Assert.Equal(1, parser.ExtractTextCalls);
    }

    [Fact]
    public async Task ExtractTextAsync_TemplateParser_ReturnsNormalizedText()
    {
        // Arrange
        var parser = new RecordingParser();

        // Act
        var result = await parser.ExtractTextAsync(StreamOf("x"));

        // Assert — Normalize hook saw the raw extraction and its output is returned
        Assert.Equal(RawText, parser.NormalizeInput);
        Assert.Equal(RawText.Trim(), result);
    }

    [Fact]
    public async Task ExtractTextAsync_TemplateParser_ReturnsChunkHookFedNormalizedText()
    {
        // Arrange
        var parser = new RecordingParser();

        // Act
        await parser.ExtractTextAsync(StreamOf("x"));

        // Assert — shared chunk hook sits AFTER normalization in the template
        Assert.Equal(RawText.Trim(), parser.ChunkInput);
    }

    [Fact]
    public void DocumentParserBase_DefaultNormalize_CrlfPaddedText_ReturnsTrimmedLineFeedText()
    {
        // Arrange
        var parser = new DefaultHookParser();

        // Act — shared default: trims and converts CRLF to LF
        var result = parser.RunNormalize("  line one\r\nline two  ");

        // Assert
        Assert.Equal("line one\nline two", result);
    }

    [Fact]
    public void DocumentParserBase_DefaultChunk_NormalizedText_ReturnsSingleChunkWithFullContent()
    {
        // Arrange
        var parser = new DefaultHookParser();

        // Act — shared default: one chunk carrying the full normalized text
        var chunks = parser.RunChunk("the normalized text");

        // Assert
        var chunk = Assert.Single(chunks);
        Assert.Equal("the normalized text", chunk.Content);
        Assert.Equal(string.Empty, chunk.Title);
    }

    [Theory]
    [InlineData(typeof(TxtDocumentParser))]
    [InlineData(typeof(DocxDocumentParser))]
    [InlineData(typeof(PdfDocumentParser))]
    [InlineData(typeof(ExcelDocumentParser))]
    public void DocumentParser_WhenRefactored_ReturnsDocumentParserBaseSubclass(Type parserType)
    {
        // Arrange / Act
        var inheritsBase = typeof(DocumentParserBase).IsAssignableFrom(parserType);

        // Assert — all four parsers move onto the template; contract stays IDocumentParser
        Assert.True(inheritsBase, $"{parserType.Name} must derive from DocumentParserBase (WP8 AC6)");
        Assert.True(typeof(IDocumentParser).IsAssignableFrom(parserType));
    }
}
