using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace StudentManagement.Application.Services;

public class PdfDocumentParser : IDocumentParser
{
    public bool CanParse(string fileName) =>
        fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var text = new System.Text.StringBuilder();

        foreach (var page in pdf.GetPages())
        {
            foreach (var word in page.GetWords())
            {
                text.Append(word.Text).Append(' ');
            }
            text.AppendLine();
        }

        return Task.FromResult(text.ToString());
    }
}
