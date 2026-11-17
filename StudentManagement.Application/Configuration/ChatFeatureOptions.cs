namespace StudentManagement.Application.Configuration
{
    public class ChatFeatureOptions
    {
        public const string SectionName = "Chat:Features";

        public bool IncludeFullDocumentation { get; set; } = false;

        public bool IncludeRag { get; set; } = false;

        public int MaxRagChunks { get; set; } = 3;

        public bool IncludeTools { get; set; } = false;

        public string EmbeddingModel { get; set; } = "nomic-embed-text";
    }
}
