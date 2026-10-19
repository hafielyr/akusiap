using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <summary>What "Periksa" shows: whether the answer is right, the correct options, the explanation and the key fact.</summary>
public sealed record AnswerFeedback(bool IsCorrect, IReadOnlyList<long> CorrectOptionIds, string Explanation, string? KeyFact);

/// <summary>Which help the attempt's rules allow (SES-05).</summary>
public sealed record PracticeRules(bool CanCheck, bool HintsEnabled)
{
    /// <summary>Checking needs Practice with "show answers after each question"; hints need Practice with hints on.</summary>
    public static PracticeRules From(string rulesSnapshot)
    {
        using var rules = JsonDocument.Parse(rulesSnapshot);
        var root = rules.RootElement;
        var practice = root.TryGetProperty("mode", out var m) && m.GetString() == nameof(AttemptMode.Practice);
        var afterEach = root.TryGetProperty("showAnswers", out var s) && s.GetString() == nameof(ShowAnswers.AfterEach);
        var hints = root.TryGetProperty("hintsEnabled", out var h) && h.ValueKind == JsonValueKind.True;
        return new PracticeRules(practice && afterEach, practice && hints);
    }
}

public enum HelpOutcome
{
    Done,

    /// <summary>Not the viewer's attempt.</summary>
    NotFound,

    /// <summary>The rules don't allow it, the item isn't answered, or the question has no hint.</summary>
    NotAllowed,

    /// <summary>The attempt is closed or its time is up.</summary>
    Ended,
}

/// <summary>
/// Practice help (SES-05, story R-01, D-68). <see cref="CheckAsync"/> scores one answered item, records CheckedAt (which
/// locks the answer) and returns the feedback; checking again returns the same feedback. <see cref="HintAsync"/> returns
/// the hint and records HintUsed. Simulation never gets either.
/// </summary>
public sealed class PracticeHelp(IAppDbContext db, ICurrentUser currentUser, IClock clock, AttemptFinisher finisher)
{
    public async Task<(HelpOutcome Outcome, AnswerFeedback? Feedback)> CheckAsync(long attemptId, Guid? guestToken, int position, CancellationToken cancellationToken)
    {
        var (outcome, attempt, item) = await LoadAsync(attemptId, guestToken, position, cancellationToken).ConfigureAwait(false);
        if (outcome != HelpOutcome.Done)
        {
            return (outcome, null);
        }

        if (!PracticeRules.From(attempt!.RulesSnapshot).CanCheck || item!.SelectedOptions.Count == 0)
        {
            return (HelpOutcome.NotAllowed, null);
        }

        var feedback = Feedback(item);
        if (item.CheckedAt is null)
        {
            item.CheckedAt = clock.UtcNow;
            item.IsCorrect = feedback.IsCorrect;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return (HelpOutcome.Done, feedback);
    }

    public async Task<(HelpOutcome Outcome, string? Hint)> HintAsync(long attemptId, Guid? guestToken, int position, CancellationToken cancellationToken)
    {
        var (outcome, attempt, item) = await LoadAsync(attemptId, guestToken, position, cancellationToken).ConfigureAwait(false);
        if (outcome != HelpOutcome.Done)
        {
            return (outcome, null);
        }

        if (!PracticeRules.From(attempt!.RulesSnapshot).HintsEnabled || string.IsNullOrWhiteSpace(item!.Question!.Hint))
        {
            return (HelpOutcome.NotAllowed, null);
        }

        if (!item.HintUsed)
        {
            item.HintUsed = true;
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return (HelpOutcome.Done, item.Question.Hint);
    }

    /// <summary>The feedback for an item, from its saved selection.</summary>
    public static AnswerFeedback Feedback(AttemptItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var question = item.Question!;
        var correct = question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToList();
        var isCorrect = Scoring.IsCorrect(question.QuestionType, correct, [.. item.SelectedOptions.Select(s => s.QuestionOptionId)]);
        return new AnswerFeedback(isCorrect, correct, question.Explanation, question.KeyFact);
    }

    private async Task<(HelpOutcome, Attempt?, AttemptItem?)> LoadAsync(long attemptId, Guid? guestToken, int position, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.SingleOrDefaultAsync(a => a.Id == attemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is null || !AttemptAccess.IsOwner(currentUser, attempt.UserId, attempt.GuestToken, guestToken))
        {
            return (HelpOutcome.NotFound, null, null);
        }

        if (await finisher.CloseIfOverdueAsync(attempt, clock.UtcNow, cancellationToken).ConfigureAwait(false))
        {
            return (HelpOutcome.Ended, null, null);
        }

        var item = await db.AttemptItems
            .Include(i => i.SelectedOptions)
            .Include(i => i.Question!).ThenInclude(q => q.Options)
            .SingleOrDefaultAsync(i => i.AttemptId == attemptId && i.Position == position, cancellationToken)
            .ConfigureAwait(false);
        return item is null ? (HelpOutcome.NotAllowed, null, null) : (HelpOutcome.Done, attempt, item);
    }
}
