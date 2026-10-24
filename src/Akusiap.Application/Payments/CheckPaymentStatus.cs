using Akusiap.Application.Abstractions;
using Akusiap.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Akusiap.Application.Payments;

/// <summary>
/// The fallback for a missed notification (PAY-04, D-111): while a payment is Pending, the payment page asks the gateway
/// for its status, at most once per <see cref="Interval"/> per payment, and a final status (paid, expired, failed) is
/// applied through <see cref="ApplyGatewayNotification"/>: stored, applied once and fulfilled exactly as a webhook would
/// be. This keeps payments moving when DOKU can't reach the app (a dev machine, or a webhook outage). Gateway errors
/// are left to the caller; the webhook remains the main path.
/// </summary>
public sealed class CheckPaymentStatus(IAppDbContext db, IPaymentGateway gateway, ApplyGatewayNotification apply, IMemoryCache cache)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <summary>True when the gateway's answer changed the payment.</summary>
    public async Task<bool> HandleAsync(long paymentId, CancellationToken cancellationToken)
    {
        var key = $"payment-status-check:{paymentId}";
        if (cache.TryGetValue(key, out _))
        {
            return false;
        }

        cache.Set(key, true, Interval);
        var payment = await db.Payments.AsNoTracking()
            .Where(p => p.Id == paymentId)
            .Select(p => new { p.InvoiceNumber, p.Status, p.Gateway })
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (payment is not { Status: PaymentStatus.Pending } || payment.Gateway != gateway.Name || string.IsNullOrEmpty(payment.InvoiceNumber))
        {
            return false;
        }

        var check = await gateway.CheckStatusAsync(payment.InvoiceNumber, cancellationToken).ConfigureAwait(false);
        if (check is null || check.Event.Kind == GatewayEventKind.Other)
        {
            return false;
        }

        var (outcome, _) = await apply.HandleCheckedStatusAsync(check, cancellationToken).ConfigureAwait(false);
        return outcome == NotificationOutcome.Applied;
    }
}
