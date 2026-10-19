using Akusiap.Application.Common;
using Akusiap.Domain.Content;

namespace Akusiap.Application.Questions;

/// <summary>An answer option as typed in the editor. <paramref name="Id"/> is set for options that already exist.</summary>
public sealed record OptionRequest(long? Id, string? Text, bool IsCorrect);

/// <summary>Create (Id null) or edit an app question (ADM-01). Options are saved in the given order.</summary>
public sealed record SaveQuestionRequest(
    long? Id,
    long TopicId,
    QuestionType? Type,
    string? Text,
    string? Explanation,
    string? KeyFact,
    string? Hint,
    short? Difficulty,
    string? SourceNote,
    QuestionStatus? Status,
    IReadOnlyList<OptionRequest> Options);

/// <summary>The rules a question must meet before it is saved (ADM-T2). Pure; returns every problem at once.</summary>
public static class QuestionValidator
{
    public const string TopicField = "TopicId";
    public const string TypeField = "QuestionType";
    public const string TextField = "Text";
    public const string ExplanationField = "Explanation";
    public const string KeyFactField = "KeyFact";
    public const string HintField = "Hint";
    public const string DifficultyField = "Difficulty";
    public const string SourceNoteField = "SourceNote";
    public const string StatusField = "Status";
    public const string OptionsField = "Options";

    public const int TextMaxLength = 4000;
    public const int ExplanationMaxLength = 4000;
    public const int HintMaxLength = 1000;
    public const int SourceNoteMaxLength = 500;
    public const int OptionMaxLength = 1000;

    /// <summary>The fixed options of a TrueFalse question, in this order.</summary>
    public const string TrueText = "Benar";
    public const string FalseText = "Salah";

    public const string TopicRequired = "Admin.Questions.TopicRequired";
    public const string TypeRequired = "Admin.Questions.TypeRequired";
    public const string StatusRequired = "Admin.Questions.StatusRequired";
    public const string DifficultyRange = "Admin.Questions.DifficultyRange";

    /// <summary>Args: type label key, minimum, maximum.</summary>
    public const string OptionCount = "Admin.Questions.OptionCount";

    /// <summary>Arg: type label key.</summary>
    public const string ExactlyOneCorrect = "Admin.Questions.ExactlyOneCorrect";
    public const string AtLeastOneCorrect = "Admin.Questions.AtLeastOneCorrect";
    public const string TrueFalseOptions = "Admin.Questions.TrueFalseOptions";

    /// <summary>Arg: option number (1-based).</summary>
    public const string OptionTextRequired = "Admin.Questions.OptionTextRequired";

    /// <summary>Args: option number, maximum length.</summary>
    public const string OptionTooLong = "Admin.Questions.OptionTooLong";

    /// <summary>Arg: option number.</summary>
    public const string OptionDuplicate = "Admin.Questions.OptionDuplicate";

    public const string TextLabel = "Admin.Question.Text";
    public const string ExplanationLabel = "Admin.Question.Explanation";
    public const string KeyFactLabel = "Admin.Question.KeyFact";
    public const string HintLabel = "Admin.Question.Hint";
    public const string SourceNoteLabel = "Admin.Question.SourceNote";

    public static string TypeLabel(QuestionType type) => $"Admin.QuestionTypeName.{type}";

    public static (int Min, int Max) OptionCountFor(QuestionType type) => type switch
    {
        QuestionType.SingleChoice => (2, 6),
        QuestionType.MultiSelect => (2, 8),
        QuestionType.TrueFalse => (2, 2),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    public static List<Problem> Validate(SaveQuestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new List<Problem>();

        if (request.TopicId <= 0)
        {
            problems.Add(new Problem(TopicRequired, TopicField));
        }

        if (request.Type is not { } type || !Enum.IsDefined(type))
        {
            problems.Add(new Problem(TypeRequired, TypeField));
        }

        if (request.Status is not { } status || !Enum.IsDefined(status))
        {
            problems.Add(new Problem(StatusRequired, StatusField));
        }

        Require(problems, TextField, TextLabel, request.Text, TextMaxLength);
        Require(problems, ExplanationField, ExplanationLabel, request.Explanation, ExplanationMaxLength);
        Limit(problems, KeyFactField, KeyFactLabel, request.KeyFact, Question.KeyFactMaxLength);
        Limit(problems, HintField, HintLabel, request.Hint, HintMaxLength);
        Limit(problems, SourceNoteField, SourceNoteLabel, request.SourceNote, SourceNoteMaxLength);

        if (request.Difficulty is { } difficulty && (difficulty < Question.MinDifficulty || difficulty > Question.MaxDifficulty))
        {
            problems.Add(new Problem(DifficultyRange, DifficultyField));
        }

        ValidateOptionTexts(problems, request.Options);
        if (request.Type is { } knownType && Enum.IsDefined(knownType))
        {
            ValidateOptionsForType(problems, knownType, request.Options);
        }

        return problems;
    }

    private static void ValidateOptionTexts(List<Problem> problems, IReadOnlyList<OptionRequest> options)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < options.Count; i++)
        {
            var number = i + 1;
            if (TextRules.Clean(options[i].Text) is not { } text)
            {
                problems.Add(new Problem(OptionTextRequired, OptionsField, number));
            }
            else if (text.Length > OptionMaxLength)
            {
                problems.Add(new Problem(OptionTooLong, OptionsField, number, OptionMaxLength));
            }
            else if (!seen.Add(text))
            {
                problems.Add(new Problem(OptionDuplicate, OptionsField, number));
            }
        }
    }

    private static void ValidateOptionsForType(List<Problem> problems, QuestionType type, IReadOnlyList<OptionRequest> options)
    {
        var (min, max) = OptionCountFor(type);
        var correct = options.Count(o => o.IsCorrect);

        if (type == QuestionType.TrueFalse)
        {
            var isFixedPair = options.Count == 2
                && TextRules.Clean(options[0].Text) == TrueText
                && TextRules.Clean(options[1].Text) == FalseText;
            if (!isFixedPair)
            {
                problems.Add(new Problem(TrueFalseOptions, OptionsField));
                return;
            }
        }
        else if (options.Count < min || options.Count > max)
        {
            problems.Add(new Problem(OptionCount, OptionsField, TypeLabel(type), min, max));
            return;
        }

        if (type == QuestionType.MultiSelect)
        {
            if (correct < 1)
            {
                problems.Add(new Problem(AtLeastOneCorrect, OptionsField));
            }
        }
        else if (correct != 1)
        {
            problems.Add(new Problem(ExactlyOneCorrect, OptionsField, TypeLabel(type)));
        }
    }

    private static void Require(List<Problem> problems, string field, string label, string? value, int maxLength)
    {
        if (TextRules.Clean(value) is not { } text)
        {
            problems.Add(new Problem(TextRules.Required, field, label));
        }
        else if (text.Length > maxLength)
        {
            problems.Add(new Problem(TextRules.TooLong, field, label, maxLength));
        }
    }

    private static void Limit(List<Problem> problems, string field, string label, string? value, int maxLength)
    {
        if (TextRules.Clean(value) is { } text && text.Length > maxLength)
        {
            problems.Add(new Problem(TextRules.TooLong, field, label, maxLength));
        }
    }
}
