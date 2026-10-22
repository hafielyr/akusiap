using System.Globalization;
using Akusiap.Application.Common;
using Akusiap.Application.RulePresets;
using Akusiap.Domain.Content;

namespace Akusiap.Web.Areas.Admin.Pages.Presets;

/// <summary>
/// Preset form fields, posted as <c>Input.*</c>. Limits are kept as typed text: blank means "no limit", and anything
/// that isn't a whole number is reported rather than silently treated as blank (which would make an exam untimed).
/// </summary>
public sealed class PresetInput
{
    public const string WholeNumber = "Validation.WholeNumber";

    public string? Name { get; set; }

    public string? TimeLimitMinutes { get; set; }

    public string? QuestionCount { get; set; }

    public string? MaxAttempts { get; set; }

    public string? PassMarkPercent { get; set; }

    public bool HintsEnabled { get; set; }

    public bool ShuffleQuestions { get; set; }

    public bool ShuffleOptions { get; set; }

    public ShowAnswers? ShowAnswers { get; set; }

    public bool IsDefault { get; set; }

    /// <summary>Simulations use the full-screen Tryout CAT layout (TRY-01).</summary>
    public bool CatMode { get; set; }

    public static PresetInput Blank() => new()
    {
        HintsEnabled = true,
        ShuffleQuestions = true,
        ShuffleOptions = true,
        ShowAnswers = Domain.Content.ShowAnswers.AfterSubmit,
    };

    public static PresetInput From(RulePresetDetail preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return new PresetInput
        {
            Name = preset.Name,
            TimeLimitMinutes = Text(preset.TimeLimitMinutes),
            QuestionCount = Text(preset.QuestionCount),
            MaxAttempts = Text(preset.MaxAttempts),
            PassMarkPercent = Text(preset.PassMarkPercent),
            HintsEnabled = preset.HintsEnabled,
            ShuffleQuestions = preset.ShuffleQuestions,
            ShuffleOptions = preset.ShuffleOptions,
            ShowAnswers = preset.ShowAnswers,
            IsDefault = preset.IsDefault,
            CatMode = preset.CatMode,
        };
    }

    /// <summary>The use-case request, plus a problem for each limit that isn't blank or a whole number.</summary>
    public (SaveRulePresetRequest Request, List<Problem> NumberProblems) ToRequest(long? id)
    {
        var problems = new List<Problem>();
        var request = new SaveRulePresetRequest(
            id,
            Name,
            Parse(TimeLimitMinutes, RulePresetValidator.TimeLimitField, RulePresetValidator.TimeLimitLabel, problems),
            Parse(QuestionCount, RulePresetValidator.QuestionCountField, RulePresetValidator.QuestionCountLabel, problems),
            HintsEnabled,
            ShuffleQuestions,
            ShuffleOptions,
            Parse(MaxAttempts, RulePresetValidator.MaxAttemptsField, RulePresetValidator.MaxAttemptsLabel, problems),
            Parse(PassMarkPercent, RulePresetValidator.PassMarkField, RulePresetValidator.PassMarkLabel, problems),
            ShowAnswers,
            IsDefault,
            CatMode);
        return (request, problems);
    }

    private static string? Text(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static int? Parse(string? text, string field, string label, List<Problem> problems)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        problems.Add(new Problem(WholeNumber, field, label));
        return null;
    }
}
