namespace StudentManagement.Application.Configuration
{
    public class ChromaOptions
    {
        public const string SectionName = "Chroma";

        public string Endpoint { get; set; } = "http://localhost:8000";

        public string CollectionName { get; set; } = "student-management";
    }
}