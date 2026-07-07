namespace StudentManagement.Application.Interfaces
{
    public interface IAppKnowledgeService
    {
        string GetDocumentation();
        string SearchRelevantChunks(string query, int maxChunks = 3);
    }
}
