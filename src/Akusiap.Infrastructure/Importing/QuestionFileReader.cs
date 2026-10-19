using System.Globalization;
using System.Text;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Application.Questions.Import;
using ClosedXML.Excel;
using CsvHelper;
using CsvHelper.Configuration;

namespace Akusiap.Infrastructure.Importing;

/// <summary>
/// Reads question rows from xlsx (first worksheet, ClosedXML) or csv (CsvHelper, UTF-8, comma or semicolon detected).
/// The first row holds the headers; fully blank rows are skipped; row numbers are the spreadsheet's (header = 1).
/// </summary>
internal sealed class QuestionFileReader : IQuestionFileReader
{
    public QuestionFileContent Read(Stream stream, string fileName, int maxRows)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var extension = Path.GetExtension(fileName ?? string.Empty).ToUpperInvariant();
        try
        {
            return extension switch
            {
                ".XLSX" => FromTable(ReadXlsx(stream, maxRows)),
                ".CSV" => FromTable(ReadCsv(stream, maxRows)),
                _ => QuestionFileContent.Fail(new Problem(IQuestionFileReader.UnsupportedFile)),
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or CsvHelperException or IOException or ArgumentException or FormatException)
        {
            return QuestionFileContent.Fail(new Problem(IQuestionFileReader.UnreadableFile));
        }
    }

    /// <summary>Headers plus (row number, cells) for every non-blank data row, up to <paramref name="maxRows"/> + 1 rows.</summary>
    private sealed record Table(IReadOnlyList<string> Headers, IReadOnlyList<(int Row, IReadOnlyList<string?> Cells)> Rows);

    private static Table ReadXlsx(Stream stream, int maxRows)
    {
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null)
        {
            return new Table([], []);
        }

        var lastColumn = used.LastColumn().ColumnNumber();
        var lastRow = used.LastRow().RowNumber();
        var headerRow = used.FirstRow().RowNumber();
        var headers = Enumerable.Range(1, lastColumn).Select(c => CellText(sheet.Cell(headerRow, c)) ?? string.Empty).ToList();
        var rows = new List<(int, IReadOnlyList<string?>)>();
        for (var r = headerRow + 1; r <= lastRow && rows.Count <= maxRows; r++)
        {
            var cells = Enumerable.Range(1, lastColumn).Select(c => CellText(sheet.Cell(r, c))).ToList();
            if (cells.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                rows.Add((r, cells));
            }
        }

        return new Table(headers, rows);
    }

    /// <summary>Cell text without culture surprises: numbers always use a dot (12.5), blanks are null.</summary>
    private static string? CellText(IXLCell cell) =>
        cell.Value.IsBlank ? null : cell.Value.ToString(CultureInfo.InvariantCulture);

    private static Table ReadCsv(Stream stream, int maxRows)
    {
        using var text = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            DetectDelimiterValues = [",", ";", "\t"],
            BadDataFound = null,
            MissingFieldFound = null,
        };
        using var csv = new CsvReader(text, config);
        if (!csv.Read())
        {
            return new Table([], []);
        }

        csv.ReadHeader();
        var headers = csv.HeaderRecord ?? [];
        var rows = new List<(int, IReadOnlyList<string?>)>();
        while (rows.Count <= maxRows && csv.Read())
        {
            var cells = Enumerable.Range(0, headers.Length).Select(i => csv.TryGetField<string>(i, out var value) ? value : null).ToList();
            if (cells.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                rows.Add((csv.Parser.Row, cells));
            }
        }

        return new Table(headers, rows);
    }

    private static QuestionFileContent FromTable(Table table)
    {
        var index = new Dictionary<string, int>();
        for (var i = 0; i < table.Headers.Count; i++)
        {
            index.TryAdd(QuestionImportColumns.Normalize(table.Headers[i]), i);
        }

        var missing = QuestionImportColumns.Required.Where(c => !index.ContainsKey(QuestionImportColumns.Normalize(c))).ToList();
        if (missing.Count > 0)
        {
            return QuestionFileContent.Fail(new Problem(IQuestionFileReader.MissingColumns, null, string.Join(", ", missing)));
        }

        string? Cell(IReadOnlyList<string?> cells, string column) =>
            index.TryGetValue(QuestionImportColumns.Normalize(column), out var i) && i < cells.Count ? cells[i] : null;

        var rows = table.Rows
            .Select(r => new QuestionImportRow(
                r.Row,
                Cell(r.Cells, QuestionImportColumns.Type),
                Cell(r.Cells, QuestionImportColumns.Text),
                [.. QuestionImportColumns.OptionColumns.Select(c => Cell(r.Cells, c))],
                Cell(r.Cells, QuestionImportColumns.Correct),
                Cell(r.Cells, QuestionImportColumns.Explanation),
                Cell(r.Cells, QuestionImportColumns.KeyFact),
                Cell(r.Cells, QuestionImportColumns.Hint),
                Cell(r.Cells, QuestionImportColumns.Topic),
                Cell(r.Cells, QuestionImportColumns.Difficulty),
                Cell(r.Cells, QuestionImportColumns.SourceNote)))
            .ToList();
        return new QuestionFileContent(rows, null);
    }
}
