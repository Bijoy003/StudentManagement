namespace StudentManagement.Application.Configuration
{
    public class ChatFeatureOptions
    {
        public const string SectionName = "Chat:Features";

        public bool IncludeFullDocumentation { get; set; } = true;

        public bool IncludeRag { get; set; } = true;

        public int MaxRagChunks { get; set; } = 3;

        public bool IncludeTools { get; set; } = true;
    }
}
