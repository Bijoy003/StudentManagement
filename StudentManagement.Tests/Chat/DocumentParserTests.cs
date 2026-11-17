using System.Text;
using NPOI.XSSF.UserModel;
using StudentManagement.Application.Services;

namespace StudentManagement.Tests;

public class DocumentParserTests
{
    [Theory]
    [InlineData("notes.txt", true)]
    [InlineData("README.MD", true)]
    [InlineData("guide.pdf", false)]
    [InlineData("sheet.xlsx", false)]
    [InlineData("", false)]
    public void TxtParser_CanParse_ReturnsExpectedResult(string fileName, bool expected)
    {
        var parser = new TxtDocumentParser();

        Assert.Equal(expected, parser.CanParse(fileName));
    }

    [Theory]
    [InlineData("report.docx", true)]
    [InlineData("REPORT.DOCX", true)]
    [InlineData("report.pdf", false)]
    public void DocxParser_CanParse_ReturnsExpectedResult(string fileName, bool expected)
    {
        var parser = new DocxDocumentParser();

        Assert.Equal(expected, parser.CanParse(fileName));
    }

    [Theory]
    [InlineData("report.pdf", true)]
    [InlineData("REPORT.PDF", true)]
    [InlineData("report.docx", false)]
    public void PdfParser_CanParse_ReturnsExpectedResult(string fileName, bool expected)
    {
        var parser = new PdfDocumentParser();

        Assert.Equal(expected, parser.CanParse(fileName));
    }

    [Theory]
    [InlineData("report.xlsx", true)]
    [InlineData("report.xls", true)]
    [InlineData("REPORT.XLSX", true)]
    [InlineData("report.csv", false)]
    public void ExcelParser_CanParse_ReturnsExpectedResult(string fileName, bool expected)
    {
        var parser = new ExcelDocumentParser();

        Assert.Equal(expected, parser.CanParse(fileName));
    }

    [Fact]
    public async Task TxtParser_ExtractTextAsync_CrlfContent_ReturnsNormalizedText()
    {
        var parser = new TxtDocumentParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("  line one\r\nline two  "));

        var text = await parser.ExtractTextAsync(stream);

        Assert.Equal("line one\nline two", text);
    }

    [Fact]
    public async Task TxtParser_ExtractTextAsync_EmptyStream_ReturnsEmptyString()
    {
        var parser = new TxtDocumentParser();
        using var stream = new MemoryStream();

        var text = await parser.ExtractTextAsync(stream);

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public async Task DocxParser_ExtractTextAsync_EmbeddedGuide_ReturnsAdvancedReportingText()
    {
        var parser = new DocxDocumentParser();
        using var stream = OpenResource(".docx");

        var text = await parser.ExtractTextAsync(stream);

        Assert.Contains("Advanced Reporting Guide", text);
        Assert.Contains("Student Count per Course Report", text);
    }

    [Fact]
    public async Task PdfParser_ExtractTextAsync_EmbeddedGuide_ReturnsSecurityText()
    {
        var parser = new PdfDocumentParser();
        using var stream = OpenResource(".pdf");

        var text = await parser.ExtractTextAsync(stream);

        Assert.Contains("Security Guide", text);
        Assert.Contains("Authentication and Authorization", text);
    }

    [Fact]
    public async Task ExcelParser_ExtractTextAsync_GeneratedWorkbook_ReturnsSheetAndCellText()
    {
        var parser = new ExcelDocumentParser();
        using var stream = CreateWorkbookStream(
            new[] { "Name", "Credits" },
            new[] { "Algebra", "3" },
            new[] { "Physics", "4" });

        var text = await parser.ExtractTextAsync(stream);

        Assert.Contains("--- Sheet: Grades ---", text);
        Assert.Contains("Name | Credits", text);
        Assert.Contains("Algebra | 3", text);
        Assert.Contains("Physics | 4", text);
    }

    [Fact]
    public async Task ExcelParser_ExtractTextAsync_EmptySheet_ReturnsSheetHeaderOnly()
    {
        var parser = new ExcelDocumentParser();
        using var stream = CreateWorkbookStream();

        var text = await parser.ExtractTextAsync(stream);

        Assert.Equal("--- Sheet: Grades ---", text);
    }

    [Fact]
    public async Task DocxParser_ExtractTextAsync_CorruptStream_ReturnsParseError()
    {
        var parser = new DocxDocumentParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip archive"));

        await Assert.ThrowsAnyAsync<Exception>(() => parser.ExtractTextAsync(stream));
    }

    [Fact]
    public async Task PdfParser_ExtractTextAsync_CorruptStream_ReturnsParseError()
    {
        var parser = new PdfDocumentParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a pdf"));

        await Assert.ThrowsAnyAsync<Exception>(() => parser.ExtractTextAsync(stream));
    }

    [Fact]
    public async Task ExcelParser_ExtractTextAsync_CorruptStream_ReturnsParseError()
    {
        var parser = new ExcelDocumentParser();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a workbook"));

        await Assert.ThrowsAnyAsync<Exception>(() => parser.ExtractTextAsync(stream));
    }

    private static Stream OpenResource(string extension)
    {
        var assembly = typeof(AppKnowledgeService).Assembly;
        var name = assembly
            .GetManifestResourceNames()
            .Single(resource => resource.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

        return assembly.GetManifestResourceStream(name)!;
    }

    private static MemoryStream CreateWorkbookStream(params string[][] rows)
    {
        using var workbook = new XSSFWorkbook();
        var sheet = workbook.CreateSheet("Grades");

        for (var r = 0; r < rows.Length; r++)
        {
            var row = sheet.CreateRow(r);
            for (var c = 0; c < rows[r].Length; c++)
            {
                row.CreateCell(c).SetCellValue(rows[r][c]);
            }
        }

        var stream = new MemoryStream();
        workbook.Write(stream, leaveOpen: true);
        stream.Position = 0;
        return stream;
    }
}
