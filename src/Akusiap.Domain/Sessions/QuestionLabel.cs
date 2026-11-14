using System.Text.RegularExpressions;

namespace Akusiap.Domain.Sessions;

/// <summary>
/// What a question is about, shown on it in the player and the review (SES-12, D-138): the SKD section (TWK, TIU, TKP)
/// when its topic names one, and the topic without that code, so "Tes Wawasan Kebangsaan (TWK)" reads as section "TWK",
/// topic "Tes Wawasan Kebangsaan", and "TIU · Numerik" as "TIU", "Numerik". Either part is null when there is nothing
/// to show.
/// </summary>
public sealed partial record QuestionLabel(string? Section, string? Topic)
{
    public static QuestionLabel None { get; } = new(null, null);

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
