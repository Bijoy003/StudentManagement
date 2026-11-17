using StudentManagement.Application.Services;

namespace StudentManagement.Tests;

public class SemanticChunkerTests
{
    [Fact]
    public async Task ChunkAsync_EmptyText_ReturnsNoChunksAndNoEmbeddingCalls()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var chunker = new SemanticChunker(embeddings);

        var chunks = await chunker.ChunkAsync("Overview", "   \r\n\r\n  ");

        Assert.Empty(chunks);
        Assert.Equal(0, embeddings.Calls);
    }

    [Fact]
    public async Task ChunkAsync_SingleParagraph_ReturnsSingleChunkWithFirstSentenceTitle()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var chunker = new SemanticChunker(embeddings);

        var chunks = await chunker.ChunkAsync("Overview", "The Students page lists records. It also supports search.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("The Students page lists records", chunk.Title);
        Assert.Equal("The Students page lists records. It also supports search.", chunk.Content);
        Assert.Equal(0, embeddings.Calls);
    }

    [Fact]
    public async Task ChunkAsync_UpToWindowSizeParagraphs_ReturnsSingleChunkWithoutEmbeddingCalls()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var chunker = new SemanticChunker(embeddings);

        var chunks = await chunker.ChunkAsync(
            "Overview",
            "First paragraph.\n\nSecond paragraph.\n\nThird paragraph.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("First paragraph.\n\nSecond paragraph.\n\nThird paragraph.", chunk.Content);
        Assert.Equal(0, embeddings.Calls);
    }

    [Fact]
    public async Task ChunkAsync_SimilarParagraphs_ReturnsSingleChunk()
    {
        var embeddings = new FakeEmbeddingGenerator(_ => [1f, 0f]);
        var chunker = new SemanticChunker(embeddings);

        var text = string.Join(
            "\n\n",
            "Students list records.",
            "Students can be searched.",
            "Students have emails.",
            "Students have phone numbers.");

        var chunks = await chunker.ChunkAsync("Students", text);

        Assert.Single(chunks);
        Assert.Equal(1, embeddings.Calls);
    }

    [Fact]
    public async Task ChunkAsync_TopicShift_ReturnsTwoChunks()
    {
        var embeddings = new FakeEmbeddingGenerator(window =>
            window.Contains("Courses have teachers", StringComparison.Ordinal) ? [0f, 1f] : [1f, 0f]);
        var chunker = new SemanticChunker(embeddings);

        var text = string.Join(
            "\n\n",
            "Students list records. Students can be searched.",
            "Students have emails. Students have phones.",
            "Courses group students. Courses have credits.",
            "Courses have teachers. Courses have names.");

        var chunks = await chunker.ChunkAsync("Handbook", text);

        Assert.Equal(2, chunks.Count);
        Assert.Equal("Students list records", chunks[0].Title);
        Assert.Equal(
            "Students list records. Students can be searched.\n\n"
            + "Students have emails. Students have phones.\n\n"
            + "Courses group students. Courses have credits.",
            chunks[0].Content);
        Assert.Equal("Courses have teachers", chunks[1].Title);
        Assert.Equal("Courses have teachers. Courses have names.", chunks[1].Content);
    }

    [Fact]
    public async Task ChunkAsync_LongFirstSentence_ReturnsDocumentTitle()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var chunker = new SemanticChunker(embeddings);
        var longSentence = new string('x', 120);

        var chunks = await chunker.ChunkAsync("Long Document", longSentence + ". Tail.");

        var chunk = Assert.Single(chunks);
        Assert.Equal("Long Document", chunk.Title);
    }

    [Fact]
    public async Task ChunkAsync_ShortParagraphWithoutPeriod_ReturnsParagraphAsTitle()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var chunker = new SemanticChunker(embeddings);

        var chunks = await chunker.ChunkAsync("Overview", "Short paragraph without period");

        var chunk = Assert.Single(chunks);
        Assert.Equal("Short paragraph without period", chunk.Title);
        Assert.Equal("Short paragraph without period", chunk.Content);
    }

    [Fact]
    public async Task ChunkAsync_MultiLineParagraph_ReturnsSpaceJoinedContent()
    {
        var embeddings = new FakeEmbeddingGenerator();
        var chunker = new SemanticChunker(embeddings);

        var chunks = await chunker.ChunkAsync(
            "Overview",
            "line one\nline two\n\n\n\nsecond para");

        var chunk = Assert.Single(chunks);
        Assert.Equal("line one line two\n\nsecond para", chunk.Content);
    }

    [Fact]
    public async Task ChunkAsync_WindowsAreEmbedded_ReturnsEmbeddingCallWithWindowBatches()
    {
        var embeddings = new FakeEmbeddingGenerator(_ => [1f, 0f]);
        var chunker = new SemanticChunker(embeddings);

        await chunker.ChunkAsync(
            "Handbook",
            "One.\n\nTwo.\n\nThree.\n\nFour.");

        Assert.Equal(1, embeddings.Calls);
        Assert.Equal(2, Assert.Single(embeddings.Batches).Count);
    }
}
