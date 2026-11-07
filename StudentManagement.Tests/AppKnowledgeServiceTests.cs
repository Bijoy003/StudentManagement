using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using StudentManagement.Application.Configuration;
using StudentManagement.Application.Services;

namespace StudentManagement.Tests
{
    public class AppKnowledgeServiceTests
    {
        [Fact]
        public void GetDocumentation_LoadsEmbeddedKnowledgeFiles()
        {
            var service = CreateService();

            var documentation = service.GetDocumentation();

            Assert.Contains("Student Management", documentation);
            Assert.Contains("Students", documentation);
            Assert.Contains("Enrollments", documentation);
        }

        [Fact]
        public void GetDocumentation_CalledTwice_ReturnsCachedInstance()
        {
            var service = CreateService();

            var first = service.GetDocumentation();
            var second = service.GetDocumentation();

            // string.Join would build a new string on every call; the same instance proves the
            // in-process IMemoryCache served the second call.
            Assert.Same(first, second);
        }

        private static AppKnowledgeService CreateService()
        {
            var mockEmbedding = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
            mockEmbedding
                .Setup(m => m.GenerateAsync(It.IsAny<IList<string>>(), null, default))
                .Returns<IList<string>, object?, CancellationToken>((strings, _, _) =>
                {
                    var embeddings = strings.Select(s => new Embedding<float>(new float[384])).ToList();
                    return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(embeddings));
                });

            var features = Options.Create(new ChatFeatureOptions());
            var chromaOptions = Options.Create(new ChromaOptions());

            return new AppKnowledgeService(
                mockEmbedding.Object,
                features,
                null!,
                null!,
                chromaOptions,
                new HttpClient(),
                [new TxtDocumentParser()],
                new SemanticChunker(mockEmbedding.Object),
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<AppKnowledgeService>.Instance);
        }
    }
}
