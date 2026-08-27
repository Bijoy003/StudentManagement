using Microsoft.Extensions.AI;
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

            var service = new AppKnowledgeService(
                mockEmbedding.Object,
                features,
                null!,
                null!,
                chromaOptions,
                new HttpClient(),
                [new TxtDocumentParser()],
                new SemanticChunker(mockEmbedding.Object));

            var documentation = service.GetDocumentation();

            Assert.Contains("Student Management", documentation);
            Assert.Contains("Students", documentation);
            Assert.Contains("Enrollments", documentation);
        }
    }
}