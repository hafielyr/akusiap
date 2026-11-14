using Akusiap.Application.Abstractions;
using Akusiap.Application.Memorization;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <summary>
/// After a guest registers or signs in in the same browser, their guest attempts (shared exams and trials) become the
/// account's, so they show in the history and the analysis (SHR-04, stories G-08, G-09, D-90; GST-02, D-135). The
/// guest token is cleared from them, and the wrong answers of the finished ones become review cards, as if the learner
/// had been signed in when they finished (MEM-01).
/// </summary>
public sealed class ClaimGuestAttempts(IAppDbContext db, WrongAnswerCards cards, IClock clock)
{
    public async Task<int> HandleAsync(Guid userId, Guid guestToken, CancellationToken cancellationToken)
    {
        var claimed = await db.Attempts
            .Where(a => a.GuestToken == guestToken && a.UserId == null)
            .Select(a => new { a.Id, Finished = a.Status != AttemptStatus.InProgress })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (claimed.Count == 0)
        {
            return 0;
        }

        var ids = claimed.Select(a => a.Id).ToList();
        var finished = claimed.Where(a => a.Finished).Select(a => a.Id).ToList();
        var wrong = await db.AttemptItems
            .Where(i => finished.Contains(i.AttemptId) && !i.IsCorrect)
            .Select(i => i.QuestionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        await cards.QueueAsync(userId, wrong, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await db.Attempts
            .Where(a => ids.Contains(a.Id) && a.UserId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UserId, userId).SetProperty(a => a.GuestToken, (Guid?)null), cancellationToken)
            .ConfigureAwait(false);
    }
}
