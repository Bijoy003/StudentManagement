namespace StudentManagement.Application.Services;

public interface IDocumentParser
{
    bool CanParse(string fileName);
    Task<string> ExtractTextAsync(Stream stream);
}
