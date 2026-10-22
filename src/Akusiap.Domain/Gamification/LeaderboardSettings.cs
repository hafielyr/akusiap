using Akusiap.Domain.Content;

namespace Akusiap.Domain.Gamification;

/// <summary>
/// How the public leaderboard of one catalog exam works (DRL-02, D-99). An exam without a row uses
/// <see cref="Default"/>: on, never reset, all three rank badges, no minimums. Not in schema.sql.
/// </summary>
public class LeaderboardSettings
{
    public long ExamId { get; set; }

    public Exam? Exam { get; set; }

    /// <summary>Off hides the board, the rank on the result page and the rank badges.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>On: a "this week" board that starts again every Monday 00:00 WIB, with badges for the final top ranks.</summary>
    public bool WeeklyReset { get; set; }

    public bool AwardRank1 { get; set; } = true;

    public bool AwardTop3 { get; set; } = true;

    public bool AwardTop10 { get; set; } = true;

    /// <summary>Badges only at or above this score; null for no minimum.</summary>
    public int? MinScorePercent { get; set; }

    /// <summary>Badges only when the board (or the week) has at least this many people; null for no minimum.</summary>
    public int? MinParticipants { get; set; }

    /// <summary>Set by "reset history": the boards only count attempts finished from this moment.</summary>
    public DateTime? CountsSince { get; set; }

    /// <summary>The Monday of the last week whose weekly badges were given (or skipped, for weeks before weekly reset was on).</summary>
    public DateOnly? WeeklyAwardedThrough { get; set; }

    public DateTime UpdatedAt { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public static LeaderboardSettings Default(long examId) => new() { ExamId = examId };

    /// <summary>
    /// The rank badge for someone at <paramref name="rank"/>, or null. The best badge that is switched on and covers the
    /// rank wins, so with "Juara 1" off the winner gets "3 besar". Minimum score and participants hold every badge back.
    /// </summary>
    public BadgeCode? BadgeFor(int rank, decimal scorePercent, int participants)
    {
        if (!Enabled || rank < 1 || participants < (MinParticipants ?? 1) || (MinScorePercent is { } min && scorePercent < min))
        {
            return null;
        }

        if (rank == 1 && AwardRank1)
        {
            return BadgeCode.Rank1;
        }

        if (rank <= 3 && AwardTop3)
        {
            return BadgeCode.Top3;
        }

        return rank <= 10 && AwardTop10 ? BadgeCode.Top10 : null;
    }
}

/// <summary>Leaderboard weeks (DRL-02): Monday 00:00 to the next Monday 00:00 in Jakarta (WIB).</summary>
public static class LeaderboardWeek
{
    /// <summary>The Monday of the week that <paramref name="day"/> is in.</summary>
    public static DateOnly StartOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>The Monday of the Jakarta week that the UTC instant falls in.</summary>
    public static DateOnly Of(DateTime utc) => StartOf(JakartaTime.Today(utc));

    /// <summary>The week as a UTC range, start included and end excluded.</summary>
    public static (DateTime Start, DateTime End) BoundsUtc(DateOnly monday) =>
        (JakartaTime.StartOfDayUtc(monday), JakartaTime.StartOfDayUtc(monday.AddDays(7)));
}
