namespace StudentManagement.Application.Services;

public class TxtDocumentParser : IDocumentParser
{
    public bool CanParse(string fileName) =>
        fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return reader.ReadToEndAsync();
    }
}
