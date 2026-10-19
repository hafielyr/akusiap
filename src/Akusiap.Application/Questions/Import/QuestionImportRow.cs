using Akusiap.Application.Common;

namespace Akusiap.Application.Questions.Import;

/// <summary>
/// The template's columns (ADM-T7). Headers are matched ignoring case, spaces and underscores; only <see cref="Required"/>
/// must be present.
/// </summary>
public static class QuestionImportColumns
{
    public const string Type = "type";
    public const string Text = "text";
    public const string Correct = "correct";
    public const string Explanation = "explanation";
    public const string KeyFact = "key_fact";
    public const string Hint = "hint";
    public const string Topic = "topic";
    public const string Difficulty = "difficulty";
    public const string SourceNote = "source_note";

    public const int MaxOptions = 8;

    public static IReadOnlyList<string> OptionColumns { get; } = [.. "ABCDEFGH".Select(letter => $"option_{char.ToLowerInvariant(letter)}")];

    /// <summary>Every column, in template order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Type, Text, .. OptionColumns, Correct, Explanation, KeyFact, Hint, Topic, Difficulty, SourceNote];

    public static IReadOnlyList<string> Required { get; } = [Type, Text, Correct, Explanation, Topic];

    /// <summary>"Key Fact", "key-fact" and "KEY_FACT" all become "keyfact".</summary>
    public static string Normalize(string? header) =>
        new([.. (header ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}

/// <summary>One data row as read from the file; every cell is raw text. <see cref="RowNumber"/> is the spreadsheet row.</summary>
public sealed record QuestionImportRow(
    int RowNumber,
    string? Type,
    string? Text,
    IReadOnlyList<string?> Options,
    string? Correct,
    string? Explanation,
    string? KeyFact,
    string? Hint,
    string? Topic,
    string? Difficulty,
    string? SourceNote);

/// <summary>The rows of an uploaded file, or why the file can't be read at all.</summary>
public sealed record QuestionFileContent(IReadOnlyList<QuestionImportRow> Rows, Problem? FileProblem)
{
    public static QuestionFileContent Fail(Problem problem) => new([], problem);
}
