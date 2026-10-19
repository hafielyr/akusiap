using System.Globalization;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;

namespace Akusiap.Application.Questions.Import;

/// <summary>A topic that imported rows can name, as "Topik" or "Subjek / Topik".</summary>
public sealed record ImportTopic(long Id, string Name, string SubjectName);

/// <summary>Finds topics by name, ignoring case; a name used in several subjects must be written with its subject.</summary>
public sealed class ImportTopicDirectory(IEnumerable<ImportTopic> topics)
{
    private readonly ILookup<string, ImportTopic> _byName = topics.ToLookup(t => t.Name.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <returns>The topic id, or the problem to report.</returns>
    public (long? Id, Problem? Problem) Find(string? text)
    {
        if (TextRules.Clean(text) is not { } value)
        {
            return (null, new Problem(QuestionValidator.TopicRequired, QuestionValidator.TopicField));
        }

        var slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash > 0)
        {
            var subject = value[..slash].Trim();
            var name = value[(slash + 1)..].Trim();
            var match = _byName[name].FirstOrDefault(t => string.Equals(t.SubjectName.Trim(), subject, StringComparison.OrdinalIgnoreCase));
            return match is null
                ? (null, new Problem(QuestionImportMapper.TopicUnknown, QuestionValidator.TopicField, value))
                : (match.Id, null);
        }

        var matches = _byName[value].ToList();
        return matches.Count switch
        {
            1 => (matches[0].Id, null),
            0 => (null, new Problem(QuestionImportMapper.TopicUnknown, QuestionValidator.TopicField, value)),
            _ => (null, new Problem(QuestionImportMapper.TopicAmbiguous, QuestionValidator.TopicField, value)),
        };
    }
}

/// <summary>
/// Turns one file row into a <see cref="SaveQuestionRequest"/> and lists every problem in it (ADM-T7, D-51): type,
/// topic, correct letters and difficulty are parsed here, then <see cref="QuestionValidator"/> checks the rest.
/// </summary>
public static class QuestionImportMapper
{
    /// <summary>Arg: the topic as written.</summary>
    public const string TopicUnknown = "Admin.Import.TopicUnknown";

    /// <summary>Arg: the topic as written.</summary>
    public const string TopicAmbiguous = "Admin.Import.TopicAmbiguous";

    /// <summary>Arg: the value as written.</summary>
    public const string CorrectInvalid = "Admin.Import.CorrectInvalid";

    /// <summary>Arg: the letter.</summary>
    public const string CorrectNoOption = "Admin.Import.CorrectNoOption";

    public const string CorrectField = "Correct";

    private static readonly Dictionary<string, QuestionType> TypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SingleChoice"] = QuestionType.SingleChoice,
        ["Pilihan ganda"] = QuestionType.SingleChoice,
        ["PG"] = QuestionType.SingleChoice,
        ["MultiSelect"] = QuestionType.MultiSelect,
        ["Pilihan ganda kompleks"] = QuestionType.MultiSelect,
        ["PGK"] = QuestionType.MultiSelect,
        ["TrueFalse"] = QuestionType.TrueFalse,
        ["Benar/Salah"] = QuestionType.TrueFalse,
        ["BS"] = QuestionType.TrueFalse,
    };

    public static (SaveQuestionRequest Request, List<Problem> Problems) Map(QuestionImportRow row, ImportTopicDirectory topics, QuestionStatus status)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(topics);
        var problems = new List<Problem>();

        QuestionType? type = TextRules.Clean(row.Type) is { } typeText && TypeNames.TryGetValue(typeText, out var parsed) ? parsed : null;
        var (topicId, topicProblem) = topics.Find(row.Topic);
        if (topicProblem is not null)
        {
            problems.Add(topicProblem);
        }

        var optionTexts = OptionTexts(row.Options, type);
        var before = problems.Count;
        var correct = ParseCorrect(row.Correct, optionTexts.Count, type, problems);
        var correctUnreadable = problems.Count > before;
        var options = optionTexts.Select((text, i) => new OptionRequest(null, text, correct.Contains(i))).ToList();

        short? difficulty = null;
        if (TextRules.Clean(row.Difficulty) is { } difficultyText)
        {
            difficulty = short.TryParse(difficultyText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : (short)-1;
        }

        var request = new SaveQuestionRequest(
            null, topicId ?? 0, type, row.Text, row.Explanation, row.KeyFact, row.Hint, difficulty, row.SourceNote, status, options);

        // Topic and correct-answer problems are already reported with clearer messages.
        problems.AddRange(QuestionValidator.Validate(request)
            .Where(p => !(topicProblem is not null && p.Code == QuestionValidator.TopicRequired))
            .Where(p => !(correctUnreadable && p.Code is QuestionValidator.ExactlyOneCorrect or QuestionValidator.AtLeastOneCorrect)));
        return (request, problems);
    }

    /// <summary>Options A–H up to the last one filled in; True/False rows may leave them empty ("Benar", "Salah").</summary>
    private static List<string?> OptionTexts(IReadOnlyList<string?> cells, QuestionType? type)
    {
        var last = -1;
        for (var i = 0; i < cells.Count; i++)
        {
            if (TextRules.Clean(cells[i]) is not null)
            {
                last = i;
            }
        }

        if (last < 0 && type == QuestionType.TrueFalse)
        {
            return [QuestionValidator.TrueText, QuestionValidator.FalseText];
        }

        return [.. cells.Take(last + 1).Select(TextRules.Clean)];
    }

    /// <summary>Letters such as "A", "A,C", "A C" or "AC" (0-based indexes); True/False also takes "Benar"/"Salah".</summary>
    private static HashSet<int> ParseCorrect(string? value, int optionCount, QuestionType? type, List<Problem> problems)
    {
        var result = new HashSet<int>();
        if (TextRules.Clean(value) is not { } text)
        {
            return result; // the validator reports the missing correct answer
        }

        if (type == QuestionType.TrueFalse)
        {
            if (string.Equals(text, QuestionValidator.TrueText, StringComparison.OrdinalIgnoreCase) || string.Equals(text, "True", StringComparison.OrdinalIgnoreCase))
            {
                return [0];
            }

            if (string.Equals(text, QuestionValidator.FalseText, StringComparison.OrdinalIgnoreCase) || string.Equals(text, "False", StringComparison.OrdinalIgnoreCase))
            {
                return [1];
            }
        }

        foreach (var c in text)
        {
            if (c is ',' or ';' or ' ')
            {
                continue;
            }

            var letter = char.ToUpperInvariant(c);
            if (letter is < 'A' or > 'H')
            {
                problems.Add(new Problem(CorrectInvalid, CorrectField, text));
                return [];
            }

            var index = letter - 'A';
            if (index >= optionCount)
            {
                problems.Add(new Problem(CorrectNoOption, CorrectField, letter.ToString()));
                return [];
            }

            result.Add(index);
        }

        return result;
    }
}
