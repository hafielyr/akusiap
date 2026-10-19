using Akusiap.Domain.Content;

namespace Akusiap.Domain.Sessions;

/// <summary>A question the viewer may get, in exam order, with its topic and option ids (in their saved order).</summary>
public sealed record CandidateQuestion(long QuestionId, long TopicId, QuestionType Type, IReadOnlyList<long> OptionIds);

/// <summary>A question placed in an attempt: its 1-based position and the order its options are shown in.</summary>
public sealed record SelectedQuestion(long QuestionId, int Position, IReadOnlyList<long> OptionOrder);

/// <summary>
/// Picks and orders an attempt's questions (SES-01). The random source is passed in, so tests can use a seeded one.
/// </summary>
public static class QuestionSelection
{
    /// <summary>
    /// Keeps the questions in <paramref name="topicIds"/> (all when empty), shuffles them when asked, then takes
    /// <paramref name="count"/> (all when null). Without shuffling, the first questions in exam order are taken.
    /// Options are shuffled when asked, except True/False, which always reads "Benar, Salah".
    /// </summary>
    public static IReadOnlyList<SelectedQuestion> Select(
        IReadOnlyList<CandidateQuestion> candidates,
        IReadOnlyCollection<long> topicIds,
        int? count,
        bool shuffleQuestions,
        bool shuffleOptions,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(topicIds);
        ArgumentNullException.ThrowIfNull(random);

        var pool = Filter(candidates, topicIds).ToList();
        if (shuffleQuestions)
        {
            Shuffle(pool, random);
        }

        var take = count is { } n ? Math.Min(n, pool.Count) : pool.Count;
        var selected = new List<SelectedQuestion>(take);
        for (var i = 0; i < take; i++)
        {
            var question = pool[i];
            var options = question.OptionIds.ToList();
            if (shuffleOptions && question.Type != QuestionType.TrueFalse)
            {
                Shuffle(options, random);
            }

            selected.Add(new SelectedQuestion(question.QuestionId, i + 1, options));
        }

        return selected;
    }

    /// <summary>How many candidates are in the chosen topics (all when none are chosen).</summary>
    public static int CountAvailable(IReadOnlyList<CandidateQuestion> candidates, IReadOnlyCollection<long> topicIds) =>
        Filter(candidates, topicIds).Count();

    /// <summary>Fisher–Yates shuffle in place.</summary>
    public static void Shuffle<T>(IList<T> items, Random random)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(random);
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    private static IEnumerable<CandidateQuestion> Filter(IReadOnlyList<CandidateQuestion> candidates, IReadOnlyCollection<long> topicIds) =>
        topicIds.Count == 0 ? candidates : candidates.Where(c => topicIds.Contains(c.TopicId));
}
