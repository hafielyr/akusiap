using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <param name="Position">The item (1-based).</param>
/// <param name="Selected">The chosen option ids; empty clears the answer.</param>
/// <param name="SecondsSpent">Seconds on this item since the last save (capped per save).</param>
public sealed record SaveAnswerRequest(long AttemptId, Guid? GuestToken, int Position, IReadOnlyList<long> Selected, bool IsFlagged, int SecondsSpent);

public enum SaveAnswerOutcome
{
    Saved,

    /// <summary>No such attempt for this viewer (the page answers 404).</summary>
    NotFound,

    /// <summary>The options don't belong to the question, or a single-answer question got several.</summary>
    Invalid,

    /// <summary>The attempt was submitted or its time is up (the page answers 409).</summary>
    Ended,
}

/// <summary>
/// Autosave (SES-04, story S-06, D-62): an idempotent upsert of one item's selected options and flag. Sets IsAnswered,
/// AnsweredAt, TimeSpentSeconds and the attempt's LastSavedAt. Refused once the attempt is no longer in progress or its
/// deadline (StartedAt + time limit) has passed.
/// </summary>
public sealed class SaveAnswer(IAppDbContext db, ICurrentUser currentUser, IClock clock, AttemptFinisher finisher)
{
    public const int MaxSecondsPerSave = 3600;

    public async Task<SaveAnswerOutcome> HandleAsync(SaveAnswerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.Id == request.AttemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is null || !AttemptAccess.IsOwner(currentUser, attempt.UserId, attempt.GuestToken, request.GuestToken))
        {
            return SaveAnswerOutcome.NotFound;
        }

        var now = clock.UtcNow;
        // A save past the deadline also closes the attempt (D-09).
        if (await finisher.CloseIfOverdueAsync(attempt, now, cancellationToken).ConfigureAwait(false))
        {
            return SaveAnswerOutcome.Ended;
        }

        var item = await db.AttemptItems
            .Include(i => i.SelectedOptions)
            .Include(i => i.Question!).ThenInclude(q => q.Options)
            .SingleOrDefaultAsync(i => i.AttemptId == attempt.Id && i.Position == request.Position, cancellationToken)
            .ConfigureAwait(false);
        if (item is null || item.CheckedAt is not null)
        {
            // A checked answer is locked (SES-05).
            return SaveAnswerOutcome.Invalid;
        }

        var selected = request.Selected.Distinct().ToList();
        var optionIds = item.Question!.Options.Select(o => o.Id).ToHashSet();
        if (selected.Any(id => !optionIds.Contains(id)) || (item.Question.QuestionType != QuestionType.MultiSelect && selected.Count > 1))
        {
            return SaveAnswerOutcome.Invalid;
        }

        var current = item.SelectedOptions.Select(s => s.QuestionOptionId).ToHashSet();
        if (!current.SetEquals(selected))
        {
            foreach (var removed in item.SelectedOptions.Where(s => !selected.Contains(s.QuestionOptionId)).ToList())
            {
                item.SelectedOptions.Remove(removed);
            }

            foreach (var added in selected.Where(id => !current.Contains(id)))
            {
                item.SelectedOptions.Add(new AttemptItemOption { QuestionOptionId = added });
            }

            item.AnsweredAt = selected.Count > 0 ? now : null;
        }

        var seconds = Math.Clamp(request.SecondsSpent, 0, MaxSecondsPerSave);
        item.IsAnswered = selected.Count > 0;
        item.IsFlagged = request.IsFlagged;
        item.TimeSpentSeconds += seconds;
        attempt.TimeSpentSeconds += seconds;
        attempt.LastSavedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return SaveAnswerOutcome.Saved;
    }
}

/// <summary>The viewer's attempt on an exam that is still in progress, for the "continue" banner (SES-04).</summary>
public sealed class FindOpenAttempt(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<long?> HandleAsync(long examId, Guid? guestToken, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null && guestToken is null)
        {
            return null;
        }

        var open = await db.Attempts
            .Where(a => a.ExamId == examId && a.Status == AttemptStatus.InProgress
                && (userId != null ? a.UserId == userId : a.UserId == null && a.GuestToken == guestToken))
            .OrderByDescending(a => a.StartedAt)
            .Select(a => new { a.Id, a.RulesSnapshot, a.StartedAt })
            .Take(5)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        return open.FirstOrDefault(a => AttemptAccess.Deadline(a.RulesSnapshot, a.StartedAt) is not { } deadline || deadline > now)?.Id;
    }
}
