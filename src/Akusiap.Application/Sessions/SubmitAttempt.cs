using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <summary>
/// Scores and closes an attempt (SES-06, D-02, D-67): IsCorrect per item (unanswered = wrong), CorrectCount, TotalCount,
/// ScorePercent, Passed (when the rules have a pass mark), TimeSpentSeconds (wall time from start, capped at the time
/// limit) and the final status with SubmittedAt. Closing an attempt that is no longer in progress changes nothing.
/// </summary>
public sealed class AttemptFinisher(IAppDbContext db)
{
    public async Task FinishAsync(Attempt attempt, AttemptStatus status, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.Status != AttemptStatus.InProgress)
        {
            return;
        }

        var items = await db.AttemptItems
            .Include(i => i.SelectedOptions)
            .Where(i => i.AttemptId == attempt.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var questionIds = items.Select(i => i.QuestionId).ToList();
        var questions = await db.Questions
            .Where(q => questionIds.Contains(q.Id))
            .Select(q => new { q.Id, q.QuestionType, Correct = q.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList() })
            .ToDictionaryAsync(q => q.Id, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<bool>(items.Count);
        foreach (var item in items)
        {
            var question = questions[item.QuestionId];
            item.IsCorrect = Scoring.IsCorrect(question.QuestionType, question.Correct, [.. item.SelectedOptions.Select(s => s.QuestionOptionId)]);
            results.Add(item.IsCorrect);
        }

        var score = Scoring.Total(results, PassMark(attempt.RulesSnapshot));
        var deadline = AttemptAccess.Deadline(attempt.RulesSnapshot, attempt.StartedAt);
        var end = deadline is { } d && d < at ? d : at;
        attempt.CorrectCount = score.CorrectCount;
        attempt.TotalCount = score.TotalCount;
        attempt.ScorePercent = score.ScorePercent;
        attempt.Passed = score.Passed;
        attempt.TimeSpentSeconds = (int)Math.Max(0, (end - attempt.StartedAt).TotalSeconds);
        attempt.SubmittedAt = end;
        attempt.Status = status;
        attempt.LastSavedAt = at;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the attempt if its deadline has passed (any access past the deadline auto-submits, D-09).
    /// Returns true when the attempt is (now) closed.
    /// </summary>
    public async Task<bool> CloseIfOverdueAsync(Attempt attempt, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (attempt.Status != AttemptStatus.InProgress)
        {
            return true;
        }

        if (AttemptAccess.Deadline(attempt.RulesSnapshot, attempt.StartedAt) is { } deadline && now > deadline)
        {
            await FinishAsync(attempt, AttemptStatus.Submitted, now, cancellationToken).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static int? PassMark(string rulesSnapshot)
    {
        using var rules = JsonDocument.Parse(rulesSnapshot);
        return rules.RootElement.TryGetProperty("passMarkPercent", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
    }
}

/// <summary>The learner submits (or the timer runs out). Idempotent: a second submit leaves the result alone.</summary>
public sealed class SubmitAttempt(IAppDbContext db, AttemptFinisher finisher, ICurrentUser currentUser, IClock clock)
{
    /// <returns>False when the attempt doesn't exist for this viewer.</returns>
    public async Task<bool> HandleAsync(long attemptId, Guid? guestToken, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.Id == attemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is null || !AttemptAccess.IsOwner(currentUser, attempt.UserId, attempt.GuestToken, guestToken))
        {
            return false;
        }

        await finisher.FinishAsync(attempt, AttemptStatus.Submitted, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Auto-submits the viewer's attempt when its deadline has passed; used before showing or saving it.</summary>
    public async Task CloseIfOverdueAsync(long attemptId, Guid? guestToken, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.Id == attemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is not null && AttemptAccess.IsOwner(currentUser, attempt.UserId, attempt.GuestToken, guestToken))
        {
            await finisher.CloseIfOverdueAsync(attempt, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// The sweep (SES-06): closes InProgress attempts whose deadline has passed as Expired (scored). The ids come from the
/// Infrastructure job, which finds them with SQL on the rules snapshot.
/// </summary>
public sealed class ExpireAttempts(IAppDbContext db, AttemptFinisher finisher, IClock clock)
{
    public async Task<int> HandleAsync(IReadOnlyList<long> attemptIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attemptIds);
        var now = clock.UtcNow;
        var closed = 0;
        foreach (var id in attemptIds)
        {
            var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.Id == id, cancellationToken).ConfigureAwait(false);
            if (attempt is { Status: AttemptStatus.InProgress }
                && AttemptAccess.Deadline(attempt.RulesSnapshot, attempt.StartedAt) is { } deadline && now > deadline)
            {
                await finisher.FinishAsync(attempt, AttemptStatus.Expired, now, cancellationToken).ConfigureAwait(false);
                closed++;
            }
        }

        return closed;
    }
}
