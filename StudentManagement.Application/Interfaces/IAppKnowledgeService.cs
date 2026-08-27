namespace StudentManagement.Application.Interfaces
{
    public interface IAppKnowledgeService
    {
        Task InitializeAsync();
        string GetDocumentation();
        Task<string> SearchRelevantChunks(string query, int maxChunks = 3);
    }
}
