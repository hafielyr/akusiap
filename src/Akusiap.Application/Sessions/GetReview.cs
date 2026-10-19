using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

public enum ReviewFilter
{
    All,
    Wrong,
    Flagged,
}

public sealed record ReviewOption(long Id, string Text, bool IsCorrect, bool Selected);

public sealed record ReviewItem(int Position, QuestionType Type, string Text, IReadOnlyList<ReviewOption> Options, bool IsCorrect, bool IsFlagged, string Explanation, string? KeyFact);

/// <param name="Hidden">The rules say answers are never shown (ShowAnswers = Never).</param>
public sealed record AttemptReview(long AttemptId, string Title, bool Hidden, int WrongCount, int FlaggedCount, IReadOnlyList<ReviewItem> Items, bool HasExam = true);

/// <summary>
/// The answer review of a finished attempt (SES-08, stories R-02, G-03, M-03, D-74): every item in its position with
/// the options in the attempt's stored order, the learner's choices, the correct options, the explanation and the key fact.
/// Filter all, wrong or flagged. Retired questions still render. Hidden when the rules say answers are never shown. Only
/// the attempt's owner (or its guest browser) gets it, and only once it is finished.
/// </summary>
public sealed class GetReview(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<AttemptReview?> HandleAsync(long attemptId, Guid? guestToken, ReviewFilter filter, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.AsNoTracking().Where(a => a.Id == attemptId)
            .Select(a => new { a.UserId, a.GuestToken, a.Status, a.RulesSnapshot, a.ExamId, Title = a.Exam == null ? null : a.Exam.Title })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (attempt is null || attempt.Status == AttemptStatus.InProgress || !AttemptAccess.IsOwner(currentUser, attempt.UserId, attempt.GuestToken, guestToken))
        {
            return null;
        }

        if (IsHidden(attempt.RulesSnapshot))
        {
            return new AttemptReview(attemptId, attempt.Title ?? string.Empty, true, 0, 0, [], attempt.ExamId is not null);
        }

        var rows = await db.AttemptItems.AsNoTracking()
            .Where(i => i.AttemptId == attemptId)
            .OrderBy(i => i.Position)
            .Select(i => new
            {
                i.Position,
                i.Question!.QuestionType,
                i.Question.Text,
                i.Question.Explanation,
                i.Question.KeyFact,
                i.OptionOrder,
                i.IsCorrect,
                i.IsFlagged,
                Options = i.Question.Options.Select(o => new { o.Id, o.Text, o.IsCorrect }).ToList(),
                Selected = i.SelectedOptions.Select(s => s.QuestionOptionId).ToList(),
            })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var items = rows.Select(r =>
        {
            var order = string.IsNullOrEmpty(r.OptionOrder) ? [] : JsonSerializer.Deserialize<List<long>>(r.OptionOrder) ?? [];
            var options = r.Options
                .OrderBy(o => order.IndexOf(o.Id) is var i and >= 0 ? i : int.MaxValue).ThenBy(o => o.Id)
                .Select(o => new ReviewOption(o.Id, o.Text, o.IsCorrect, r.Selected.Contains(o.Id)))
                .ToList();
            return new ReviewItem(r.Position, r.QuestionType, r.Text, options, r.IsCorrect, r.IsFlagged, r.Explanation, r.KeyFact);
        }).ToList();

        var filtered = filter switch
        {
            ReviewFilter.Wrong => items.Where(i => !i.IsCorrect),
            ReviewFilter.Flagged => items.Where(i => i.IsFlagged),
            _ => items,
        };
        return new AttemptReview(attemptId, attempt.Title ?? string.Empty, false, items.Count(i => !i.IsCorrect), items.Count(i => i.IsFlagged), [.. filtered], attempt.ExamId is not null);
    }

    private static bool IsHidden(string rulesSnapshot)
    {
        using var rules = JsonDocument.Parse(rulesSnapshot);
        return rules.RootElement.TryGetProperty("showAnswers", out var s) && s.GetString() == nameof(ShowAnswers.Never);
    }
}
