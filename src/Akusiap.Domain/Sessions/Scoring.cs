using Akusiap.Domain.Content;

namespace Akusiap.Domain.Sessions;

/// <summary>An attempt's result.</summary>
/// <param name="ScorePercent">0–100, two decimals.</param>
/// <param name="Passed">Null when the rules have no pass mark.</param>
public sealed record AttemptScore(int CorrectCount, int TotalCount, decimal ScorePercent, bool? Passed);

/// <summary>
/// Scoring (SES-06, D-02). Single choice and True/False are correct when exactly the one correct option is chosen;
/// multi-select is all or nothing: the chosen set must equal the correct set. Unanswered questions are wrong.
/// </summary>
public static class Scoring
{
    public static bool IsCorrect(QuestionType type, IReadOnlyCollection<long> correctOptions, IReadOnlyCollection<long> selectedOptions)
    {
        ArgumentNullException.ThrowIfNull(correctOptions);
        ArgumentNullException.ThrowIfNull(selectedOptions);
        if (selectedOptions.Count == 0)
        {
            return false;
        }

        var selected = selectedOptions.ToHashSet();
        return type == QuestionType.MultiSelect
            ? selected.SetEquals(correctOptions)
            : selected.Count == 1 && correctOptions.Count == 1 && selected.SetEquals(correctOptions);
    }

    /// <summary>Totals the item results; the percentage is rounded half up to two decimals.</summary>
    public static AttemptScore Total(IReadOnlyCollection<bool> itemResults, int? passMarkPercent)
    {
        ArgumentNullException.ThrowIfNull(itemResults);
        var total = itemResults.Count;
        var correct = itemResults.Count(r => r);
        var percent = total == 0 ? 0m : Math.Round(100m * correct / total, 2, MidpointRounding.AwayFromZero);
        bool? passed = passMarkPercent is { } mark ? percent >= mark : null;
        return new AttemptScore(correct, total, percent, passed);
    }
}
