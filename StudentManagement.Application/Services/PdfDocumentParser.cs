using UglyToad.PdfPig;

namespace StudentManagement.Application.Services;

public class PdfDocumentParser : DocumentParserBase
{
    public override bool CanParse(string fileName) =>
        fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);

    protected override string ExtractText(Stream stream)
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

        return text.ToString();
    }
}
