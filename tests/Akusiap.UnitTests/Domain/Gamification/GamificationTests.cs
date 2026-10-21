using Akusiap.Domain.Gamification;

namespace Akusiap.UnitTests.Domain.Gamification;

public sealed class LeaderboardTests
{
    private static readonly DateTime T0 = new(2026, 10, 22, 2, 0, 0, DateTimeKind.Utc);

    private static LeaderboardCandidate C(string name, decimal score, int seconds, int minutesLater = 0, long id = 0) =>
        new(Guid.NewGuid(), name, false, score, seconds, T0.AddMinutes(minutesLater), id);

    [Fact]
    public void Higher_score_wins_then_less_time_then_earlier_finish()
    {
        var rows = Leaderboard.Rank([C("slow", 80, 900), C("fast", 80, 600), C("best", 95, 1500), C("late", 80, 600, minutesLater: 5)]);

        Assert.Equal(["best", "fast", "late", "slow"], rows.Select(r => r.Entry.DisplayName));
    }

    [Fact]
    public void Same_score_and_time_share_a_rank()
    {
        var rows = Leaderboard.Rank([C("a", 90, 600), C("b", 80, 500), C("c", 80, 500, minutesLater: 1), C("d", 70, 400)]);

        Assert.Equal([1, 2, 2, 4], rows.Select(r => r.Rank));
    }

    [Fact]
    public void Hidden_names_show_a_stable_masked_number()
    {
        var id = Guid.Parse("a1b2c3d4-0000-0000-0000-000000000000");

        Assert.Equal("Peserta #A1B2", Leaderboard.PublicName(id, "Sari", hideName: true));
        Assert.Equal("Sari", Leaderboard.PublicName(id, "Sari", hideName: false));
    }
}

public sealed class JourneyTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    [Theory]
    [InlineData(0, 1, 0, 100)]
    [InlineData(99, 1, 99, 100)]
    [InlineData(100, 2, 0, 200)]
    [InlineData(299, 2, 199, 200)]
    [InlineData(300, 3, 0, 300)]
    [InlineData(1000, 5, 0, 500)]
    public void Levels_need_100_more_xp_each_time(int xp, int level, int into, int next)
    {
        var progress = Journey.Level(xp);

        Assert.Equal((level, into, next), (progress.Level, progress.XpIntoLevel, progress.XpForNextLevel));
    }

    [Fact]
    public void Xp_counts_correct_answers_attempts_reviews_and_badges()
    {
        var stats = new JourneyStats(FinishedAttempts: 2, AnsweredQuestions: 30, CorrectAnswers: 20, 80m, false, FlashcardReviews: 10, 0, 0);

        Assert.Equal((20 * 10) + (2 * 20) + (10 * 2) + (3 * 50), Journey.Xp(stats, badges: 3));
    }

    [Fact]
    public void A_streak_survives_until_a_whole_day_is_missed()
    {
        DateOnly[] days = [Today.AddDays(-5), Today.AddDays(-3), Today.AddDays(-2), Today.AddDays(-1)];

        var yesterday = Journey.Streak(days, Today);
        var withToday = Journey.Streak([.. days, Today], Today);
        var broken = Journey.Streak(days, Today.AddDays(1));

        Assert.Equal((3, 3, false), (yesterday.Current, yesterday.Longest, yesterday.ActiveToday));
        Assert.Equal((4, 4, true), (withToday.Current, withToday.Longest, withToday.ActiveToday));
        Assert.Equal(0, broken.Current);
        Assert.Equal(3, broken.Longest);
    }

    [Fact]
    public void Milestones_follow_the_stats()
    {
        var none = Journey.EarnedMilestones(new JourneyStats(0, 0, 0, 0m, false, 0, 0, 0));
        var many = Journey.EarnedMilestones(new JourneyStats(5, 520, 400, 92m, true, 60, 7, 1));

        Assert.Empty(none);
        Assert.Equal(
            [BadgeCode.FirstSteps, BadgeCode.Questions100, BadgeCode.Questions500, BadgeCode.Sharpshooter, BadgeCode.PassedSimulation, BadgeCode.Streak3, BadgeCode.Streak7, BadgeCode.Flashcards50, BadgeCode.ClassJoined],
            many);
    }

    [Theory]
    [InlineData(1, BadgeCode.Rank1)]
    [InlineData(2, BadgeCode.Top3)]
    [InlineData(3, BadgeCode.Top3)]
    [InlineData(4, BadgeCode.Top10)]
    [InlineData(10, BadgeCode.Top10)]
    public void Rank_badges_cover_the_top_ten(int rank, BadgeCode badge) => Assert.Equal(badge, Journey.RankBadge(rank));

    [Fact]
    public void No_rank_badge_outside_the_top_ten() => Assert.Null(Journey.RankBadge(11));
}
