using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Access;

/// <summary>
/// Nightly (PAY-08, D-06, D-77): Active exam purchases and packages whose end has passed become Expired, so admin and
/// learner views match what <see cref="IEntitlementService"/> already enforces. A package in a failed-renewal grace
/// period stays Active until the grace ends.
/// </summary>
public sealed class ExpireAccess(IAppDbContext db, IClock clock)
{
    public const string Id = "expire-access";

    /// <summary>00:05 WIB (17:05 UTC).</summary>
    public const string Cron = "5 17 * * *";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var purchases = await db.ExamPurchases
            .Where(p => p.Status == ExamPurchaseStatus.Active && p.AccessExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, ExamPurchaseStatus.Expired), cancellationToken)
            .ConfigureAwait(false);
        var subscriptions = await db.Subscriptions
            .Where(s => s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd <= now && (s.GraceUntil == null || s.GraceUntil <= now))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, SubscriptionStatus.Expired), cancellationToken)
            .ConfigureAwait(false);
        return purchases + subscriptions;
    }
}
