using StudentManagement.Application.Services;

namespace StudentManagement.Tests
{
    public class AppKnowledgeServiceTests
    {
        [Fact]
        public void GetDocumentation_LoadsEmbeddedKnowledgeFiles()
        {
            var service = new AppKnowledgeService();

            var documentation = service.GetDocumentation();

            Assert.Contains("Student Management", documentation);
            Assert.Contains("Students", documentation);
            Assert.Contains("Enrollments", documentation);
        }

        [Fact]
        public void SearchRelevantChunks_ReturnsMatchingSections()
        {
            var service = new AppKnowledgeService();

            var result = service.SearchRelevantChunks("how do I enroll a student in a course");

            Assert.Contains("Enrollments", result, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SearchRelevantChunks_ReturnsEmpty_ForUnrelatedQuery()
        {
            var service = new AppKnowledgeService();

            var result = service.SearchRelevantChunks("xyzzy");

            Assert.Equal(string.Empty, result);
        }
    }
}
