using NPOI.XWPF.UserModel;

namespace StudentManagement.Application.Services;

public class DocxDocumentParser : DocumentParserBase
{
    public override bool CanParse(string fileName) =>
        fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase);

    protected override string ExtractText(Stream stream)
    {
        using var doc = new XWPFDocument(stream);
        var text = new System.Text.StringBuilder();

        foreach (var para in doc.Paragraphs)
        {
            var line = para.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                text.AppendLine(line);
            }
        }

        foreach (var table in doc.Tables)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.GetTableCells())
                {
                    var cellText = cell.GetText()?.Trim();
                    if (!string.IsNullOrWhiteSpace(cellText))
                    {
                        text.Append(cellText).Append(" ");
                    }
                }
                text.AppendLine();
            }
        }

        return text.ToString();
    }
}
