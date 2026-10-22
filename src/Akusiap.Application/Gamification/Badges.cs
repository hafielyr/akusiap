using Akusiap.Application.Abstractions;
using Akusiap.Domain;
using Akusiap.Domain.Gamification;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Gamification;

public sealed record EarnedBadge(BadgeCode Code, long? ExamId, string? ExamTitle, DateTime AwardedAt, DateOnly? WeekStart = null);

/// <summary>The learner's journey page (GAM-01): level, streak, counts and badges.</summary>
public sealed record JourneyView(LevelProgress Level, StreakInfo Streak, JourneyStats Stats, IReadOnlyList<EarnedBadge> Badges);

/// <summary>
/// The learning journey and badges (GAM-01, D-95). Stats are counted from finished attempts (not flashcard sets),
/// flashcard reviews and class memberships; streaks use Jakarta days with any finished attempt or review. Milestone
/// badges are awarded once when an event could have earned them (an attempt finishing, a flashcard rating, joining a
/// class); a rank badge is awarded when a counted Simulation attempt finishes in the top 10 of its leaderboard, as the exam's
/// leaderboard settings allow (DRL-02; weekly boards give theirs at week end, see <see cref="WeeklyLeaderboardAwards"/>).
/// </summary>
public sealed class Badges(IAppDbContext db, Leaderboards leaderboards, IClock clock)
{
    /// <summary>Awards every badge the user has newly earned, plus a rank badge for <paramref name="attemptId"/>. Returns the new ones.</summary>
    public async Task<IReadOnlyList<BadgeCode>> AwardAsync(Guid userId, long? attemptId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var (stats, _) = await StatsAsync(userId, cancellationToken).ConfigureAwait(false);
        var owned = await db.UserBadges.Where(b => b.UserId == userId && b.WeekStart == null).Select(b => new { b.Code, b.ExamId }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var added = new List<UserBadge>();
        foreach (var code in Journey.EarnedMilestones(stats).Where(c => !owned.Any(o => o.Code == c && o.ExamId == null)))
        {
            added.Add(new UserBadge { UserId = userId, Code = code, AwardedAt = now });
        }

        if (attemptId is { } id && await leaderboards.StandingAsync(id, cancellationToken).ConfigureAwait(false) is { } standing
            && standing.Badge is { } rankBadge && !owned.Any(o => o.ExamId == standing.ExamId && Journey.IsRankBadge(o.Code)))
        {
            added.Add(new UserBadge { UserId = userId, Code = rankBadge, ExamId = standing.ExamId, AwardedAt = now });
        }

        if (added.Count == 0)
        {
            return [];
        }

        db.UserBadges.AddRange(added);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another request awarded the same badge at the same moment; the unique index keeps one. Nothing to do.
            foreach (var badge in added)
            {
                db.UserBadges.Entry(badge).State = EntityState.Detached;
            }

            return [];
        }

        return [.. added.Select(b => b.Code)];
    }

    public async Task<JourneyView> JourneyAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (stats, streak) = await StatsAsync(userId, cancellationToken).ConfigureAwait(false);
        var badges = await db.UserBadges.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.AwardedAt)
            .Select(b => new EarnedBadge(b.Code, b.ExamId, b.Exam == null ? null : b.Exam.Title, b.AwardedAt, b.WeekStart))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var level = Journey.Level(Journey.Xp(stats, badges.Count));
        return new JourneyView(level, streak, stats, badges);
    }

    /// <summary>Level and streak only, for small widgets (Today page).</summary>
    public async Task<(LevelProgress Level, StreakInfo Streak)> SummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (stats, streak) = await StatsAsync(userId, cancellationToken).ConfigureAwait(false);
        var badgeCount = await db.UserBadges.CountAsync(b => b.UserId == userId, cancellationToken).ConfigureAwait(false);
        return (Journey.Level(Journey.Xp(stats, badgeCount)), streak);
    }

    private async Task<(JourneyStats Stats, StreakInfo Streak)> StatsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var finished = db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Status != AttemptStatus.InProgress && a.SubmittedAt != null && a.Mode != AttemptMode.Flashcard);
        var totals = await finished
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Answered = g.Sum(a => a.TotalCount),
                Correct = g.Sum(a => a.CorrectCount),
                Best = g.Max(a => a.ScorePercent),
                Passed = g.Any(a => a.Mode == AttemptMode.Simulation && a.Passed == true),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var attemptRows = await finished.Select(a => new { At = a.SubmittedAt!.Value, a.Source }).ToListAsync(cancellationToken).ConfigureAwait(false);
        var attemptTimes = attemptRows.Select(r => r.At).ToList();
        var dailyGoals = attemptRows.Where(r => r.Source == AttemptSource.DailyDrill).Select(r => JakartaTime.Today(r.At)).Distinct().Count();
        var reviewTimes = await db.ReviewLogs.AsNoTracking()
            .Where(l => l.ReviewCard!.UserId == userId)
            .Select(l => l.ReviewedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var classes = await db.ClassMembers.CountAsync(m => m.UserId == userId, cancellationToken).ConfigureAwait(false);

        var streak = Journey.Streak(attemptTimes.Concat(reviewTimes).Select(JakartaTime.Today), JakartaTime.Today(clock.UtcNow));
        var stats = new JourneyStats(
            totals?.Count ?? 0,
            totals?.Answered ?? 0,
            totals?.Correct ?? 0,
            totals?.Best ?? 0m,
            totals?.Passed ?? false,
            reviewTimes.Count,
            streak.Longest,
            classes,
            dailyGoals);
        return (stats, streak);
    }
}
