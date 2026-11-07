using NPOI.SS.UserModel;

namespace StudentManagement.Application.Services;

public class ExcelDocumentParser : IDocumentParser
{
    public bool CanParse(string fileName) =>
        fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ||
        fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(Stream stream)
    {
        using var workbook = WorkbookFactory.Create(stream);
        var text = new System.Text.StringBuilder();

        for (var s = 0; s < workbook.NumberOfSheets; s++)
        {
            var sheet = workbook.GetSheetAt(s);
            if (text.Length > 0)
                text.AppendLine();

            text.AppendLine($"--- Sheet: {sheet.SheetName} ---");

            for (var r = 0; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;

                var cells = new List<string>();
                for (var c = 0; c < row.LastCellNum; c++)
                {
                    var cell = row.GetCell(c);
                    if (cell != null)
                    {
                        cells.Add(cell.ToString()?.Trim() ?? "");
                    }
                }
                text.AppendLine(string.Join(" | ", cells));
            }
        }

        return Task.FromResult(text.ToString());
    }
}
