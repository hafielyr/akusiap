using System.Text;
using Akusiap.Application.Abstractions;
using Akusiap.Infrastructure.Importing;
using ClosedXML.Excel;

namespace Akusiap.IntegrationTests.Questions;

/// <summary>ADM-T7: reading xlsx and csv question files (no database).</summary>
public sealed class QuestionFileReaderTests
{
    private readonly QuestionFileReader _reader = new();

    [Fact]
    public void The_template_reads_back_as_its_three_example_rows()
    {
        using var stream = new MemoryStream(new QuestionTemplate().Create());

        var content = _reader.Read(stream, "template.xlsx", 1000);

        Assert.Null(content.FileProblem);
        Assert.Equal([2, 3, 4], content.Rows.Select(r => r.RowNumber));
        var first = content.Rows[0];
        Assert.Equal(("SingleChoice", "A", "Jakarta", "2"), (first.Type, first.Correct, first.Options[0], first.Difficulty));
        Assert.Equal(8, first.Options.Count);
    }

    [Fact]
    public void Xlsx_rows_skip_blank_lines_and_keep_spreadsheet_row_numbers()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Soal");
        string[] headers = ["Type", "Text", "Option A", "Option B", "Correct", "Explanation", "Key Fact", "Topic"];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        sheet.Cell(2, 1).Value = "SingleChoice";
        sheet.Cell(2, 2).Value = "Satu?";
        sheet.Cell(4, 1).Value = "TrueFalse";
        sheet.Cell(4, 7).Value = "Fakta";
        sheet.Cell(4, 3).Value = 12.5;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var content = _reader.Read(stream, "SOAL.XLSX", 1000);

        Assert.Equal([2, 4], content.Rows.Select(r => r.RowNumber));
        Assert.Equal(("Fakta", "12.5"), (content.Rows[1].KeyFact, content.Rows[1].Options[0]));
        Assert.Null(content.Rows[0].Hint);
    }

    [Theory]
    [InlineData(",")]
    [InlineData(";")]
    public void Csv_with_bom_quotes_and_either_delimiter(string delimiter)
    {
        var csv = string.Join("\r\n",
            string.Join(delimiter, "type", "text", "option_a", "option_b", "correct", "explanation", "topic"),
            string.Join(delimiter, "SingleChoice", "\"Berapa 1" + delimiter + "5 + 1?\"", "\"2,5\"", "3", "A", "Karena.", "Aljabar"),
            string.Join(delimiter, "", "", "", "", "", "", ""),
            string.Join(delimiter, "TrueFalse", "Langit biru?", "", "", "Benar", "Ya.", "Umum"));
        using var stream = new MemoryStream([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv)]);

        var content = _reader.Read(stream, "soal.csv", 1000);

        Assert.Null(content.FileProblem);
        Assert.Equal([2, 4], content.Rows.Select(r => r.RowNumber));
        Assert.Equal($"Berapa 1{delimiter}5 + 1?", content.Rows[0].Text);
        Assert.Equal(("SingleChoice", "Aljabar"), (content.Rows[0].Type, content.Rows[0].Topic));
    }

    [Fact]
    public void Missing_required_columns_are_named()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("type,text,topic\r\nSingleChoice,Soal,Umum"));

        var content = _reader.Read(stream, "soal.csv", 1000);

        Assert.Equal(IQuestionFileReader.MissingColumns, content.FileProblem!.Code);
        Assert.Equal("correct, explanation", content.FileProblem.Args[0]);
    }

    [Fact]
    public void Other_extensions_and_broken_files_are_refused()
    {
        using var text = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        using var broken = new MemoryStream(Encoding.UTF8.GetBytes("not a zip"));

        Assert.Equal(IQuestionFileReader.UnsupportedFile, _reader.Read(text, "soal.txt", 1000).FileProblem!.Code);
        Assert.Equal(IQuestionFileReader.UnreadableFile, _reader.Read(broken, "soal.xlsx", 1000).FileProblem!.Code);
    }

    [Fact]
    public void Reading_stops_one_row_past_the_limit()
    {
        var csv = new StringBuilder("type,text,correct,explanation,topic\r\n");
        for (var i = 0; i < 50; i++)
        {
            csv.Append("SingleChoice,Soal,A,Ok,Umum\r\n");
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString()));

        Assert.Equal(11, _reader.Read(stream, "soal.csv", 10).Rows.Count);
    }
}
