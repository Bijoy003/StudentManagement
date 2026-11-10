namespace StudentManagement.Application.Services;

public class TxtDocumentParser : DocumentParserBase
{
    public override bool CanParse(string fileName) =>
        fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    protected override string ExtractText(Stream stream)
    {
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
