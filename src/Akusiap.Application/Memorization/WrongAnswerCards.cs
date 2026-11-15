using Akusiap.Application.Abstractions;
using Akusiap.Domain.Memorization;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Memorization;

/// <summary>
/// MEM-01 (story M-04, D-76): each wrong question becomes a review card in Learning, due at once. A card the user
/// already has (any state) goes back to Learning and due now, keeping its SM-2 history. The caller saves.
/// </summary>
public sealed class WrongAnswerCards(IAppDbContext db)
{
    public async Task QueueAsync(Guid userId, IReadOnlyCollection<long> wrongQuestionIds, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(wrongQuestionIds);
        if (wrongQuestionIds.Count == 0)
        {
            return;
        }

        var ids = wrongQuestionIds.Distinct().ToList();
        var existing = await db.ReviewCards
            .Where(c => c.UserId == userId && ids.Contains(c.QuestionId))
            .ToDictionaryAsync(c => c.QuestionId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var questionId in ids)
        {
            if (existing.TryGetValue(questionId, out var card))
            {
                card.State = ReviewCardState.Learning;
                card.DueAt = at;
            }
            else
            {
                db.ReviewCards.Add(new ReviewCard { UserId = userId, QuestionId = questionId, State = ReviewCardState.Learning, DueAt = at });
            }
        }
    }
}
