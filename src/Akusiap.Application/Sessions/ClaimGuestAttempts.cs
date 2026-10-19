using Akusiap.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <summary>
/// After a guest registers in the same browser, their guest attempts (shared exams and trials) become the new
/// account's, so they show in the history (SHR-04, stories G-08, G-09, D-90). The guest token is cleared from them.
/// </summary>
public sealed class ClaimGuestAttempts(IAppDbContext db)
{
    public async Task<int> HandleAsync(Guid userId, Guid guestToken, CancellationToken cancellationToken) =>
        await db.Attempts
            .Where(a => a.GuestToken == guestToken && a.UserId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.UserId, userId).SetProperty(a => a.GuestToken, (Guid?)null), cancellationToken)
            .ConfigureAwait(false);
}
