using Akusiap.Domain.Memorization;

namespace Akusiap.UnitTests.Domain;

/// <summary>MEM-02: SM-2 rating sequences from a new card (EF 2.5), checked against the reference rules by hand.</summary>
public sealed class Sm2SchedulerTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 3, 0, 0, DateTimeKind.Utc);

    public static TheoryData<string, ReviewRating[], decimal, decimal, int, int, ReviewCardState> Sequences => new()
    {
        // name, ratings, interval, EF, repetitions, lapses, state
        { "first good", [ReviewRating.Good], 1m, 2.50m, 1, 0, ReviewCardState.Learning },
        { "good x2", [ReviewRating.Good, ReviewRating.Good], 6m, 2.50m, 2, 0, ReviewCardState.Review },
        { "good x3", [ReviewRating.Good, ReviewRating.Good, ReviewRating.Good], 15m, 2.50m, 3, 0, ReviewCardState.Review },
        { "good x4: 15 x 2.5 = 37.5, rounded up", [ReviewRating.Good, ReviewRating.Good, ReviewRating.Good, ReviewRating.Good], 38m, 2.50m, 4, 0, ReviewCardState.Memorized },
        { "good x5", [ReviewRating.Good, ReviewRating.Good, ReviewRating.Good, ReviewRating.Good, ReviewRating.Good], 95m, 2.50m, 5, 0, ReviewCardState.Memorized },
        { "easy x3: interval uses the EF before the review", [ReviewRating.Easy, ReviewRating.Easy, ReviewRating.Easy], 17m, 2.80m, 3, 0, ReviewCardState.Review },
        { "hard x3: 6 x 2.22 = 13.32", [ReviewRating.Hard, ReviewRating.Hard, ReviewRating.Hard], 14m, 2.08m, 3, 0, ReviewCardState.Review },
        { "hard x4: 14 x 2.08 = 29.12", [ReviewRating.Hard, ReviewRating.Hard, ReviewRating.Hard, ReviewRating.Hard], 30m, 1.94m, 4, 0, ReviewCardState.Memorized },
        { "forgot on a new card is not a lapse", [ReviewRating.Forgot], 1m, 2.18m, 0, 0, ReviewCardState.Learning },
        { "forgot after learning restarts and counts a lapse", [ReviewRating.Good, ReviewRating.Good, ReviewRating.Good, ReviewRating.Forgot], 1m, 2.18m, 0, 1, ReviewCardState.Learning },
        { "relearning after a lapse: 6 x 2.18 = 13.08", [ReviewRating.Good, ReviewRating.Good, ReviewRating.Good, ReviewRating.Forgot, ReviewRating.Good, ReviewRating.Good, ReviewRating.Good], 14m, 2.18m, 3, 1, ReviewCardState.Review },
        { "EF never drops below 1.3", [ReviewRating.Forgot, ReviewRating.Forgot, ReviewRating.Forgot, ReviewRating.Forgot, ReviewRating.Forgot], 1m, 1.30m, 0, 0, ReviewCardState.Learning },
    };

    [Theory]
    [MemberData(nameof(Sequences))]
    public void Rating_sequences_follow_sm2(string name, ReviewRating[] ratings, decimal interval, decimal ease, int repetitions, int lapses, ReviewCardState state)
    {
        var current = Sm2State.New;
        var due = Now;
        foreach (var rating in ratings)
        {
            (current, due) = Sm2Scheduler.Review(current, rating, Now);
        }

        Assert.True(
            current == new Sm2State(state, interval, ease, repetitions, lapses),
            $"{name}: got {current}");
        Assert.Equal(Now.AddDays((double)interval), due);
    }

    [Theory]
    [InlineData(ReviewRating.Forgot, 2)]
    [InlineData(ReviewRating.Hard, 3)]
    [InlineData(ReviewRating.Good, 4)]
    [InlineData(ReviewRating.Easy, 5)]
    public void Buttons_map_to_sm2_grades(ReviewRating rating, int grade)
    {
        Assert.Equal(grade, Sm2Scheduler.Grade(rating));
    }

    [Fact]
    public void Ease_changes_by_the_sm2_formula()
    {
        Assert.Equal(2.60m, Sm2Scheduler.Review(Sm2State.New, ReviewRating.Easy, Now).Next.EaseFactor);
        Assert.Equal(2.50m, Sm2Scheduler.Review(Sm2State.New, ReviewRating.Good, Now).Next.EaseFactor);
        Assert.Equal(2.36m, Sm2Scheduler.Review(Sm2State.New, ReviewRating.Hard, Now).Next.EaseFactor);
        Assert.Equal(2.18m, Sm2Scheduler.Review(Sm2State.New, ReviewRating.Forgot, Now).Next.EaseFactor);
    }

    [Fact]
    public void Apply_updates_the_card_and_logs_the_review()
    {
        var card = new ReviewCard { State = ReviewCardState.Review, IntervalDays = 6m, EaseFactor = 2.5m, Repetitions = 2, DueAt = Now };

        var log = Sm2Scheduler.Apply(card, ReviewRating.Good, Now);

        Assert.Equal((ReviewCardState.Review, 15m, 3, Now.AddDays(15), (DateTime?)Now), (card.State, card.IntervalDays, card.Repetitions, card.DueAt, card.LastReviewedAt));
        Assert.Equal((ReviewRating.Good, 6m, 15m, Now), (log.Rating, log.IntervalBeforeDays, log.IntervalAfterDays, log.ReviewedAt));
        Assert.Same(log, Assert.Single(card.Logs));
    }
}
