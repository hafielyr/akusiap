using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Payments;

/// <summary>
/// Every 15 minutes (PAY-08, D-77): Pending payments past <see cref="Payment.ExpiresAt"/> become Expired, in case the
/// gateway's expiry notification never came. A purchase or package still AwaitingPayment with no other Pending payment
/// becomes Abandoned, so the next checkout starts a fresh row. Active access being renewed is left alone. At most 500
/// per run.
/// </summary>
public sealed class ExpirePendingPayments(IAppDbContext db, IClock clock)
{
    public const string Id = "expire-pending-payments";
    public const string Cron = "*/15 * * * *";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var payments = await db.Payments
            .Where(p => p.Status == PaymentStatus.Pending && p.ExpiresAt != null && p.ExpiresAt <= now)
            .OrderBy(p => p.Id)
            .Take(500)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (payments.Count == 0)
        {
            return 0;
        }

        foreach (var payment in payments)
        {
            payment.Status = PaymentStatus.Expired;
        }

        var expiredIds = payments.Select(p => p.Id).ToList();
        var purchaseIds = payments.Where(p => p.ExamPurchaseId != null).Select(p => p.ExamPurchaseId!.Value).ToList();
        var subscriptionIds = payments.Where(p => p.SubscriptionId != null).Select(p => p.SubscriptionId!.Value).ToList();

        var purchases = await db.ExamPurchases
            .Where(x => purchaseIds.Contains(x.Id) && x.Status == ExamPurchaseStatus.AwaitingPayment)
            .Where(x => !db.Payments.Any(p => p.ExamPurchaseId == x.Id && p.Status == PaymentStatus.Pending && !expiredIds.Contains(p.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        purchases.ForEach(x => x.Status = ExamPurchaseStatus.Abandoned);

        var subscriptions = await db.Subscriptions
            .Where(x => subscriptionIds.Contains(x.Id) && x.Status == SubscriptionStatus.AwaitingPayment)
            .Where(x => !db.Payments.Any(p => p.SubscriptionId == x.Id && p.Status == PaymentStatus.Pending && !expiredIds.Contains(p.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        subscriptions.ForEach(x => x.Status = SubscriptionStatus.Abandoned);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return payments.Count;
    }
}
