using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Interfaces;
using StudentManagement.Application.Services;

namespace StudentManagement.Tests;

public class AppKnowledgeServiceTests
{
    [Fact]
    public void GetDocumentation_LoadsEmbeddedKnowledgeFiles_ReturnsApplicationText()
    {
        var service = CreateService(out _, out _);

        var documentation = service.GetDocumentation();

        Assert.Contains("Student Management", documentation);
        Assert.Contains("Students", documentation);
        Assert.Contains("Enrollments", documentation);
    }

    [Fact]
    public void GetDocumentation_EmbedsEveryDocument_ReturnsBlankLineSeparatedContent()
    {
        var service = CreateService(out _, out _);

        var documentation = service.GetDocumentation();

        Assert.Contains(Environment.NewLine + Environment.NewLine, documentation);
    }

    [Fact]
    public void GetDocumentation_CalledTwice_ReturnsCachedInstance()
    {
        var service = CreateService(out _, out _);

        var first = service.GetDocumentation();
        var second = service.GetDocumentation();

        // string.Join would build a new string on every call; the same instance proves the
        // in-process IMemoryCache served the second call.
        Assert.Same(first, second);
    }

    [Fact]
    public void GetDocumentation_AllParsers_ReturnsTextFromEmbeddedBinaryDocuments()
    {
        var service = CreateService(
            out _,
            out _,
            new IDocumentParser[]
            {
                new TxtDocumentParser(),
                new DocxDocumentParser(),
                new PdfDocumentParser(),
                new ExcelDocumentParser()
            });

        var documentation = service.GetDocumentation();

        Assert.Contains("Advanced Reporting Guide", documentation);
        Assert.Contains("Security Guide", documentation);
    }

    [Fact]
    public async Task InitializeAsync_LoadsChunks_ReturnsOneEmbeddedDocumentPerChunk()
    {
        var service = CreateService(out var store, out var embeddings);

        await service.InitializeAsync();

        Assert.Equal(1, store.ReplaceCalls);
        Assert.NotEmpty(store.Documents);
        Assert.All(store.Documents, document =>
        {
            Assert.False(string.IsNullOrWhiteSpace(document.Title));
            Assert.False(string.IsNullOrWhiteSpace(document.Content));
            Assert.Equal(384, document.Embedding.Length);
        });
        Assert.NotEmpty(embeddings.Batches);
    }

    [Fact]
    public async Task InitializeAsync_CalledConcurrently_ReturnsSingleReplaceCall()
    {
        var service = CreateService(out var store, out _);

        await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => service.InitializeAsync()));

        Assert.Equal(1, store.ReplaceCalls);
    }

    [Fact]
    public async Task SearchRelevantChunks_EmptyQuery_ReturnsEmptyWithoutEmbeddingOrQuery()
    {
        var service = CreateService(out var store, out var embeddings);

        var result = await service.SearchRelevantChunks("   ", 3);

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, store.QueryCalls);
        Assert.Equal(0, embeddings.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchRelevantChunks_NonPositiveMaxChunks_ReturnsEmptyWithoutQuery(int maxChunks)
    {
        var service = CreateService(out var store, out _);

        var result = await service.SearchRelevantChunks("students", maxChunks);

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, store.QueryCalls);
    }

    [Fact]
    public async Task SearchRelevantChunks_RelevantResult_ReturnsTitleAndContent()
    {
        var service = CreateService(out var store, out _);
        store.Results.Add(new VectorSearchResult("Students", "The Students page lists records.", 0.2f));

        var result = await service.SearchRelevantChunks("students", 5);

        Assert.Equal("### Students\nThe Students page lists records.", result);
        Assert.Equal(5, store.LastMaxResults);
        Assert.Equal(384, store.LastEmbedding.Length);
    }

    [Fact]
    public async Task SearchRelevantChunks_MultipleResults_ReturnsOnlyResultsBelowThreshold()
    {
        var service = CreateService(out var store, out _);
        store.Results.AddRange(
        [
            new VectorSearchResult("Close", "close content", 0.2f),
            new VectorSearchResult("Boundary", "boundary content", 0.7f),
            new VectorSearchResult("Far", "far content", 0.9f)
        ]);

        var result = await service.SearchRelevantChunks("students", 3);

        Assert.Contains("close content", result);
        Assert.DoesNotContain("boundary content", result);
        Assert.DoesNotContain("far content", result);
    }

    [Fact]
    public async Task SearchRelevantChunks_NoResults_ReturnsEmpty()
    {
        var service = CreateService(out _, out _);

        var result = await service.SearchRelevantChunks("students", 3);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task SearchRelevantChunks_MultipleRelevantResults_ReturnsBlankLineSeparatedSections()
    {
        var service = CreateService(out var store, out _);
        store.Results.AddRange(
        [
            new VectorSearchResult("One", "first", 0.1f),
            new VectorSearchResult("Two", "second", 0.2f)
        ]);

        var result = await service.SearchRelevantChunks("students", 2);

        Assert.Equal($"### One\nfirst{Environment.NewLine}{Environment.NewLine}### Two\nsecond", result);
    }

    private static AppKnowledgeService CreateService(
        out FakeVectorStore store,
        out FakeEmbeddingGenerator embeddings,
        IDocumentParser[]? parsers = null)
    {
        store = new FakeVectorStore();
        embeddings = new FakeEmbeddingGenerator();

        var features = Options.Create(new ChatFeatureOptions());
        var chunker = new SemanticChunker(embeddings);

        return new AppKnowledgeService(
            embeddings,
            features,
            store,
            parsers ?? new IDocumentParser[] { new TxtDocumentParser() },
            chunker,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<AppKnowledgeService>.Instance);
    }
}
