using Akusiap.Application.Abstractions;
using Akusiap.Domain.Gamification;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Gamification;

/// <summary>
/// Weekly leaderboard badges (DRL-02, D-99). For every exam whose board is on and resets weekly, each week that has
/// ended since <see cref="LeaderboardSettings.WeeklyAwardedThrough"/> gets its badges from the final ranks: the
/// best badge the exam's settings allow, per person, stored with the week's Monday so it can be won again in later
/// weeks. Runs hourly (a week ends Monday 00:00 WIB) and is safe to run again: a week is only handled once, and the
/// unique index keeps one badge per person, exam and week.
/// </summary>
public sealed class WeeklyLeaderboardAwards(IAppDbContext db, Leaderboards leaderboards, IClock clock)
{
    public const string Id = "weekly-leaderboard-awards";
    public const string Cron = "5 * * * *";

    /// <summary>Gives the badges of every week that has ended. Returns how many badges were given.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var thisWeek = LeaderboardWeek.Of(now);
        var due = await db.LeaderboardSettings
            .Where(s => s.Enabled && s.WeeklyReset && (s.WeeklyAwardedThrough == null || s.WeeklyAwardedThrough < thisWeek.AddDays(-7)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var given = 0;
        foreach (var settings in due)
        {
            var week = settings.WeeklyAwardedThrough?.AddDays(7) ?? thisWeek.AddDays(-7);
            for (; week < thisWeek; week = week.AddDays(7))
            {
                given += await AwardWeekAsync(settings, week, now, cancellationToken).ConfigureAwait(false);
                settings.WeeklyAwardedThrough = week;
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return given;
    }

    private async Task<int> AwardWeekAsync(LeaderboardSettings settings, DateOnly monday, DateTime now, CancellationToken cancellationToken)
    {
        var rows = await leaderboards.WeekAsync(settings, monday, cancellationToken).ConfigureAwait(false);
        var winners = rows
            .Select(r => (r.Entry.UserId, Badge: settings.BadgeFor(r.Rank, r.Entry.ScorePercent, rows.Count)))
            .Where(w => w.Badge is not null)
            .ToList();
        if (winners.Count == 0)
        {
            return 0;
        }

        var userIds = winners.Select(w => w.UserId).ToList();
        var already = await db.UserBadges
            .Where(b => b.ExamId == settings.ExamId && b.WeekStart == monday && userIds.Contains(b.UserId))
            .Select(b => b.UserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var added = winners
            .Where(w => !already.Contains(w.UserId))
            .Select(w => new UserBadge { UserId = w.UserId, Code = w.Badge!.Value, ExamId = settings.ExamId, WeekStart = monday, AwardedAt = now })
            .ToList();
        db.UserBadges.AddRange(added);
        return added.Count;
    }
}
