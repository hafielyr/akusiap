using System.Text.RegularExpressions;
using Akusiap.Domain.Content;

namespace Akusiap.Domain.Sessions;

/// <summary>
/// What a question is about, shown on it in the player and the review (SES-12, D-138): the SKD section (TWK, TIU, TKP)
/// when its topic names one, and the topic without that code, so "Tes Wawasan Kebangsaan (TWK)" reads as section "TWK",
/// topic "Tes Wawasan Kebangsaan", and "TIU · Numerik" as "TIU", "Numerik". Either part is null when there is nothing
/// to show. <see cref="Difficulty"/> is the measured difficulty once enough learners answered it (DIF-01, D-140).
/// </summary>
public sealed partial record QuestionLabel(string? Section, string? Topic, MeasuredDifficulty? Difficulty = null)
{
    public static QuestionLabel None { get; } = new(null, null);

    public bool IsEmpty => Section is null && Topic is null && Difficulty is null;

    /// <summary>The label from the topic name and, when the question has stats, its answer counts.</summary>
    public static QuestionLabel From(string? topicName, int? correct, int? wrong, int? marked) =>
        From(topicName) with { Difficulty = correct is null ? null : QuestionDifficulty.Of(correct.Value, wrong ?? 0, marked ?? 0) };

    public static QuestionLabel From(string? topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName))
        {
            return None;
        }

        var name = topicName.Trim();
        if (SkdScoring.SubtestOf(name) is not { } subtest)
        {
            return new QuestionLabel(null, name);
        }

        var code = SkdScoring.Rule(subtest).Code;
        var escaped = Regex.Escape(code);
        var rest = Regex.Replace(name, $@"\(\s*{escaped}\s*\)|(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])", " ", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        rest = Spaces().Replace(rest, " ").Trim(' ', '·', '-', '/', ':', ',', '.');
        return new QuestionLabel(code, rest.Length == 0 ? null : rest);
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
