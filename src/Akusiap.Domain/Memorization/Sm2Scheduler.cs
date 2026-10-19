namespace Akusiap.Domain.Memorization;

/// <summary>A review card's scheduling state.</summary>
public sealed record Sm2State(ReviewCardState State, decimal IntervalDays, decimal EaseFactor, int Repetitions, int Lapses)
{
    public static Sm2State New { get; } = new(ReviewCardState.New, 0m, ReviewCard.DefaultEaseFactor, 0, 0);

    public static Sm2State Of(ReviewCard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new(card.State, card.IntervalDays, card.EaseFactor, card.Repetitions, card.Lapses);
    }
}

/// <summary>The state after a review and when the card is next due.</summary>
public sealed record Sm2Result(Sm2State Next, DateTime DueAt);

/// <summary>
/// SuperMemo SM-2 (MEM-02, story M-02; Woźniak 1987), with the four buttons mapped to SM-2 grades (D-50):
/// Forgot = 2, Hard = 3, Good = 4, Easy = 5.
/// <list type="bullet">
/// <item>Grade ≥ 3: the 1st interval is 1 day, the 2nd 6 days, then the previous interval × EF, rounded up.</item>
/// <item>Grade &lt; 3 (Forgot): repetitions start again (interval 1 day); a card that had been learned counts a lapse.</item>
/// <item>EF' = EF + (0.1 − (5 − q)(0.08 + (5 − q)·0.02)) after every review, never below 1.3, kept to two decimals.
/// The interval uses the EF from before the review.</item>
/// <item>State: Learning after a forget or the first success, Review afterwards, Memorized once the interval is ≥ 21 days.</item>
/// </list>
/// </summary>
public static class Sm2Scheduler
{
    public const decimal MinEaseFactor = 1.3m;
    public const decimal MemorizedAfterDays = 21m;

    public static int Grade(ReviewRating rating) => rating switch
    {
        ReviewRating.Forgot => 2,
        ReviewRating.Hard => 3,
        ReviewRating.Good => 4,
        ReviewRating.Easy => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(rating), rating, null),
    };

    public static Sm2Result Review(Sm2State state, ReviewRating rating, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(state);
        var q = Grade(rating);
        var ease = NextEase(state.EaseFactor, q);
        Sm2State next;
        if (q < 3)
        {
            var lapses = state.Repetitions > 0 ? state.Lapses + 1 : state.Lapses;
            next = new Sm2State(ReviewCardState.Learning, 1m, ease, 0, lapses);
        }
        else
        {
            var interval = state.Repetitions switch
            {
                0 => 1m,
                1 => 6m,
                _ => Math.Ceiling(state.IntervalDays * state.EaseFactor),
            };
            var repetitions = state.Repetitions + 1;
            var cardState = interval >= MemorizedAfterDays ? ReviewCardState.Memorized
                : repetitions == 1 ? ReviewCardState.Learning
                : ReviewCardState.Review;
            next = new Sm2State(cardState, interval, ease, repetitions, state.Lapses);
        }

        return new Sm2Result(next, now.AddDays((double)next.IntervalDays));
    }

    /// <summary>Applies a review to <paramref name="card"/> and returns the log entry to store.</summary>
    public static ReviewLog Apply(ReviewCard card, ReviewRating rating, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(card);
        var before = card.IntervalDays;
        var (next, dueAt) = Review(Sm2State.Of(card), rating, now);
        card.State = next.State;
        card.IntervalDays = next.IntervalDays;
        card.EaseFactor = next.EaseFactor;
        card.Repetitions = next.Repetitions;
        card.Lapses = next.Lapses;
        card.DueAt = dueAt;
        card.LastReviewedAt = now;
        var log = new ReviewLog { ReviewCard = card, Rating = rating, IntervalBeforeDays = before, IntervalAfterDays = next.IntervalDays, ReviewedAt = now };
        card.Logs.Add(log);
        return log;
    }

    private static decimal NextEase(decimal ease, int q)
    {
        var miss = 5 - q;
        var next = ease + (0.1m - (miss * (0.08m + (miss * 0.02m))));
        return Math.Max(MinEaseFactor, Math.Round(next, 2, MidpointRounding.AwayFromZero));
    }
}
