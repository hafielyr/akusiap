using Akusiap.Application.Common;
using Akusiap.Domain.Content;

namespace Akusiap.Application.RulePresets;

/// <summary>
/// Create (Id null) or edit a system rule preset (ADM-10). Null limits mean: untimed, all questions, unlimited attempts,
/// no pass mark.
/// </summary>
public sealed record SaveRulePresetRequest(
    long? Id,
    string? Name,
    int? TimeLimitMinutes,
    int? QuestionCount,
    bool HintsEnabled,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    int? MaxAttempts,
    int? PassMarkPercent,
    ShowAnswers? ShowAnswers,
    bool IsDefault);

/// <summary>The rules a system preset must meet before it is saved (ADM-T5). Pure; returns every problem at once.</summary>
public static class RulePresetValidator
{
    public const string NameField = "Name";
    public const string TimeLimitField = "TimeLimitMinutes";
    public const string QuestionCountField = "QuestionCount";
    public const string MaxAttemptsField = "MaxAttempts";
    public const string PassMarkField = "PassMarkPercent";
    public const string ShowAnswersField = "ShowAnswers";

    public const int MaxTimeLimitMinutes = 600;
    public const int MaxQuestionCount = 1000;
    public const int MaxAttemptsLimit = 100;

    public const string NameLabel = "Admin.Preset.Name";
    public const string TimeLimitLabel = "Admin.Preset.TimeLimit";
    public const string QuestionCountLabel = "Admin.Preset.QuestionCount";
    public const string MaxAttemptsLabel = "Admin.Preset.MaxAttempts";
    public const string PassMarkLabel = "Admin.Preset.PassMark";

    /// <summary>Args: field label key, minimum, maximum.</summary>
    public const string RangeCode = "Validation.Range";
    public const string ShowAnswersRequired = "Admin.Presets.ShowAnswersRequired";

    public static List<Problem> Validate(SaveRulePresetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new List<Problem>();
        TextRules.RequireName(problems, NameField, NameLabel, request.Name);
        Range(problems, TimeLimitField, TimeLimitLabel, request.TimeLimitMinutes, 1, MaxTimeLimitMinutes);
        Range(problems, QuestionCountField, QuestionCountLabel, request.QuestionCount, 1, MaxQuestionCount);
        Range(problems, MaxAttemptsField, MaxAttemptsLabel, request.MaxAttempts, 1, MaxAttemptsLimit);
        Range(problems, PassMarkField, PassMarkLabel, request.PassMarkPercent, 0, 100);
        if (request.ShowAnswers is not { } showAnswers || !Enum.IsDefined(showAnswers))
        {
            problems.Add(new Problem(ShowAnswersRequired, ShowAnswersField));
        }

        return problems;
    }

    private static void Range(List<Problem> problems, string field, string label, int? value, int min, int max)
    {
        if (value is { } number && (number < min || number > max))
        {
            problems.Add(new Problem(RangeCode, field, label, min, max));
        }
    }
}
