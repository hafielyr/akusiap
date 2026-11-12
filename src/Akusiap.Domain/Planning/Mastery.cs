using Akusiap.Domain.Memorization;

namespace Akusiap.Domain.Planning;

/// <summary>One topic of a goal's exam: its questions the learner can use, how many they answered and mastered.</summary>
public sealed record TopicProgress(long TopicId, string Name, int Total, int Answered, int Mastered)
{
    public int AnsweredPercent => Percent(Answered);

    public int MasteredPercent => Percent(Mastered);

    private int Percent(int count) => Total == 0 ? 0 : (int)Math.Round(count * 100m / Total, MidpointRounding.AwayFromZero);
}

/// <summary>When a question counts as mastered (PLN-04, D-122) and how topics are ranked.</summary>
public static class Mastery
{
    /// <summary>
    /// With a review card, the card decides: Review or Memorized is mastered (a wrong answer resets it to Learning,
    /// MEM-01). Without one, the last answer must have been right.
    /// </summary>
    public static bool IsMastered(bool? lastAnswerCorrect, ReviewCardState? cardState) =>
        cardState is { } state
            ? state is ReviewCardState.Review or ReviewCardState.Memorized
            : lastAnswerCorrect == true;

    /// <summary>Weakest first: the smallest mastered share, then the smallest answered share, then by name.</summary>
    public static IReadOnlyList<TopicProgress> WeakestFirst(IEnumerable<TopicProgress> topics) =>
        [.. topics
            .OrderBy(t => t.Total == 0 ? 1m : (decimal)t.Mastered / t.Total)
            .ThenBy(t => t.Total == 0 ? 1m : (decimal)t.Answered / t.Total)
            .ThenBy(t => t.Name, StringComparer.CurrentCulture)];
}
