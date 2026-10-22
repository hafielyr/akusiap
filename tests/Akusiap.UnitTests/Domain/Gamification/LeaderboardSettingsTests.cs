using Akusiap.Domain.Gamification;

namespace Akusiap.UnitTests.Domain.Gamification;

/// <summary>DRL-02: per-exam leaderboard settings decide which rank badge someone gets, and weeks run Monday to Monday in Jakarta.</summary>
public sealed class LeaderboardSettingsTests
{
    [Theory]
    [InlineData(1, BadgeCode.Rank1)]
    [InlineData(3, BadgeCode.Top3)]
    [InlineData(10, BadgeCode.Top10)]
    public void The_default_settings_give_the_usual_rank_badges(int rank, BadgeCode badge) =>
        Assert.Equal(badge, LeaderboardSettings.Default(1).BadgeFor(rank, scorePercent: 50m, participants: 1));

    [Fact]
    public void A_switched_off_badge_falls_back_to_the_next_one_that_is_on()
    {
        var settings = new LeaderboardSettings { AwardRank1 = false, AwardTop3 = true, AwardTop10 = false };

        Assert.Equal(BadgeCode.Top3, settings.BadgeFor(1, 90m, 20));
        Assert.Equal(BadgeCode.Top3, settings.BadgeFor(3, 90m, 20));
        Assert.Null(settings.BadgeFor(4, 90m, 20));
    }

    [Fact]
    public void Minimum_score_and_participants_hold_back_badges()
    {
        var settings = new LeaderboardSettings { MinScorePercent = 60, MinParticipants = 5 };

        Assert.Null(settings.BadgeFor(1, 59.9m, 10));
        Assert.Null(settings.BadgeFor(1, 90m, 4));
        Assert.Equal(BadgeCode.Rank1, settings.BadgeFor(1, 60m, 5));
    }

    [Fact]
    public void A_switched_off_leaderboard_gives_no_badges()
    {
        var settings = new LeaderboardSettings { Enabled = false };

        Assert.Null(settings.BadgeFor(1, 100m, 100));
    }

    [Theory]
    [InlineData("2026-10-19", "2026-10-19")] // Monday
    [InlineData("2026-10-22", "2026-10-19")] // Thursday
    [InlineData("2026-10-25", "2026-10-19")] // Sunday
    [InlineData("2026-10-26", "2026-10-26")] // next Monday
    public void Weeks_start_on_monday(string day, string monday) =>
        Assert.Equal(DateOnly.Parse(monday, System.Globalization.CultureInfo.InvariantCulture), LeaderboardWeek.StartOf(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void A_week_runs_from_monday_midnight_in_jakarta()
    {
        var monday = new DateOnly(2026, 10, 19);

        var (start, end) = LeaderboardWeek.BoundsUtc(monday);

        Assert.Equal(new DateTime(2026, 10, 18, 17, 0, 0, DateTimeKind.Utc), start); // Monday 00:00 WIB
        Assert.Equal(new DateTime(2026, 10, 25, 17, 0, 0, DateTimeKind.Utc), end);
        Assert.Equal(monday, LeaderboardWeek.Of(new DateTime(2026, 10, 18, 17, 0, 0, DateTimeKind.Utc)));
        Assert.Equal(monday.AddDays(-7), LeaderboardWeek.Of(new DateTime(2026, 10, 18, 16, 59, 59, DateTimeKind.Utc)));
    }
}
