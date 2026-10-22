using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Application.Sessions;
using Akusiap.Domain;
using Akusiap.Domain.Gamification;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Gamification;

public enum DrillState
{
    NotStarted,
    InProgress,
    Done,
}

/// <summary>Today's drill as the Today page shows it, with the week strip and the drill streak.</summary>
public sealed record DrillToday(DrillState State, long? AttemptId, decimal? TodayScore, IReadOnlyList<(DateOnly Day, bool Done)> Week, StreakInfo Streak, int GoalsMet);

/// <summary>
/// The daily drill (DRL-01, D-97). The daily goal is one finished drill per Jakarta day; the drill streak counts the
/// days in a row that met it. A drill is a Practice attempt (Source DailyDrill, no exam) of up to 10 questions the
/// learner can fully use (<see cref="AccessibleQuestions"/>): review cards due now first (oldest first), then
/// questions they have never had in an attempt, then the rest, each group in random order. An unfinished drill is
/// continued rather than replaced; after the goal is met, more drills are allowed.
/// </summary>
public sealed class DailyDrills(IAppDbContext db, AccessibleQuestions accessible, StartAttempt startAttempt, ICurrentUser currentUser, IClock clock, IRandomSource random)
{
    public const int Size = 10;
    public const string NothingToDrill = "Drill.Empty";

    public async Task<DrillToday> TodayAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        var today = JakartaTime.Today(clock.UtcNow);
        var drills = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Source == AttemptSource.DailyDrill)
            .Select(a => new { a.Id, a.Status, a.SubmittedAt, a.ScorePercent })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var goalDays = drills.Where(d => d.Status != AttemptStatus.InProgress && d.SubmittedAt != null)
            .Select(d => JakartaTime.Today(d.SubmittedAt!.Value))
            .ToHashSet();
        var open = drills.Where(d => d.Status == AttemptStatus.InProgress).OrderByDescending(d => d.Id).FirstOrDefault();
        var doneToday = drills.Where(d => d.SubmittedAt is { } at && JakartaTime.Today(at) == today && d.Status != AttemptStatus.InProgress)
            .OrderByDescending(d => d.ScorePercent).FirstOrDefault();
        var state = open is not null ? DrillState.InProgress : doneToday is not null ? DrillState.Done : DrillState.NotStarted;
        return new DrillToday(
            state,
            open?.Id ?? doneToday?.Id,
            doneToday?.ScorePercent,
            Journey.Week(goalDays, today),
            Journey.Streak(goalDays, today),
            goalDays.Count);
    }

    /// <summary>The drill to open: the unfinished one, or a new one. Fails when there is nothing the learner can use.</summary>
    public async Task<Result<long>> StartAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        var open = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Source == AttemptSource.DailyDrill && a.Status == AttemptStatus.InProgress)
            .OrderByDescending(a => a.Id)
            .Select(a => (long?)a.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (open is { } id)
        {
            return Result.Ok(id);
        }

        var pool = (await accessible.ForCurrentUserAsync(cancellationToken).ConfigureAwait(false)).ToList();
        if (pool.Count == 0)
        {
            return Result.Fail<long>(new Problem(NothingToDrill));
        }

        var now = clock.UtcNow;
        var due = await db.ReviewCards.AsNoTracking()
            .Where(c => c.UserId == userId && c.DueAt <= now && pool.Contains(c.QuestionId))
            .OrderBy(c => c.DueAt)
            .Select(c => c.QuestionId)
            .Take(Size)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var seen = (await db.AttemptItems.AsNoTracking()
            .Where(i => i.Attempt!.UserId == userId && pool.Contains(i.QuestionId))
            .Select(i => i.QuestionId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).ToHashSet();
        var rest = pool.Except(due).ToList();
        var picked = due
            .Concat(Shuffle(rest.Where(q => !seen.Contains(q))))
            .Concat(Shuffle(rest.Where(seen.Contains)))
            .Take(Size)
            .ToList();
        var started = await startAttempt.StartDailyDrillAsync(picked, Size, cancellationToken).ConfigureAwait(false);
        return started.Succeeded ? Result.Ok(started.Value!.AttemptId) : Result.Fail<long>(started.Problems);
    }

    private List<long> Shuffle(IEnumerable<long> ids)
    {
        var list = ids.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("The daily drill needs a signed-in user.");
}
