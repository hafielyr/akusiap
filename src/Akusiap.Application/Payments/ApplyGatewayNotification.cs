using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Payments;

public enum NotificationOutcome
{
    /// <summary>The body couldn't be read; nothing was stored.</summary>
    Unreadable,

    /// <summary>The same event was received before; nothing changed (BR-26).</summary>
    Duplicate,

    /// <summary>Stored but not trusted: the signature is wrong.</summary>
    InvalidSignature,

    /// <summary>Stored; no payment has that invoice number.</summary>
    UnknownPayment,

    /// <summary>Stored; the payment was not Pending, so its status was left alone.</summary>
    Ignored,

    /// <summary>Stored; a success for a payment already Expired or Canceled. Flagged for the admin, not fulfilled (BR-23).</summary>
    LatePayment,

    /// <summary>Stored and applied: the payment moved on (and was fulfilled when Paid).</summary>
    Applied,
}

/// <summary>
/// Stores a gateway notification and applies it (PAY-01, PAY-06, D-52, D-64). Every readable notification is kept in
/// <c>payment_webhook_events</c> with its signature check; invalid ones are never applied, and each gateway event id is
/// processed once. A Pending payment moves to Paid, Expired or Failed. On Paid, the item is fulfilled in the same save:
/// the exam purchase becomes Active until (running end, else now) + the exam's access days (D-04), or the subscription
/// becomes Active until (running end, else now) + the package's months (D-03). A success for a payment that already
/// Expired or was Canceled only sets <see cref="Payment.IsLatePayment"/>; what to do with it is the owner's call.
/// </summary>
public sealed class ApplyGatewayNotification(IAppDbContext db, IPaymentGateway gateway, IClock clock, IPaymentJobs jobs)
{
    public async Task<(NotificationOutcome Outcome, Payment? Payment)> HandleAsync(GatewayNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var parsed = gateway.ParseNotification(notification);
        if (parsed is null)
        {
            return (NotificationOutcome.Unreadable, null);
        }

        return await StoreAndApplyAsync(parsed, notification.Body, () => gateway.VerifySignature(notification), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a status the app fetched from the gateway's own API (<see cref="IPaymentGateway.CheckStatusAsync"/>): the
    /// request was signed with our keys and answered over HTTPS, so it is trusted like a verified notification. It is
    /// stored and de-duplicated the same way.
    /// </summary>
    public Task<(NotificationOutcome Outcome, Payment? Payment)> HandleCheckedStatusAsync(GatewayStatusCheck check, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(check);
        return StoreAndApplyAsync(check.Event, check.Body, () => true, cancellationToken);
    }

    private async Task<(NotificationOutcome Outcome, Payment? Payment)> StoreAndApplyAsync(
        GatewayEvent parsed, string payload, Func<bool> verify, CancellationToken cancellationToken)
    {
        if (await db.PaymentWebhookEvents.AnyAsync(e => e.Gateway == gateway.Name && e.GatewayEventId == parsed.EventId, cancellationToken).ConfigureAwait(false))
        {
            return (NotificationOutcome.Duplicate, null);
        }

        var now = clock.UtcNow;
        var signatureValid = verify();
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.InvoiceNumber == parsed.InvoiceNumber, cancellationToken).ConfigureAwait(false);
        var stored = new PaymentWebhookEvent
        {
            Gateway = gateway.Name,
            GatewayEventId = parsed.EventId,
            PaymentId = payment?.Id,
            Payload = payload,
            SignatureValid = signatureValid,
            ReceivedAt = now,
        };
        db.PaymentWebhookEvents.Add(stored);

        var outcome = await ApplyAsync(parsed, payment, signatureValid, now, stored, cancellationToken).ConfigureAwait(false);
        try
        {
            // One SaveChanges: the event, the payment and the fulfilment commit together.
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Lost a race with the same event: the other request handled it.
            if (await db.PaymentWebhookEvents.AsNoTracking().AnyAsync(e => e.Gateway == gateway.Name && e.GatewayEventId == parsed.EventId, cancellationToken).ConfigureAwait(false))
            {
                return (NotificationOutcome.Duplicate, null);
            }

            throw;
        }

        if (outcome == NotificationOutcome.Applied && payment is { Status: PaymentStatus.Paid })
        {
            await jobs.QueueReceiptAsync(payment.Id, cancellationToken).ConfigureAwait(false);
        }

        return (outcome, payment);
    }

    private async Task<NotificationOutcome> ApplyAsync(
        GatewayEvent parsed, Payment? payment, bool signatureValid, DateTime now, PaymentWebhookEvent stored, CancellationToken cancellationToken)
    {
        if (!signatureValid)
        {
            stored.ProcessingError = "Invalid signature.";
            return NotificationOutcome.InvalidSignature;
        }

        stored.ProcessedAt = now;
        if (payment is null)
        {
            stored.ProcessingError = "No payment has this invoice number.";
            return NotificationOutcome.UnknownPayment;
        }

        if (parsed.Kind == GatewayEventKind.Paid && payment.Status is PaymentStatus.Expired or PaymentStatus.Canceled)
        {
            payment.IsLatePayment = true;
            stored.ProcessingError = $"Late payment: the payment was {payment.Status}. Not fulfilled; the admin decides.";
            return NotificationOutcome.LatePayment;
        }

        if (payment.Status != PaymentStatus.Pending || parsed.Kind == GatewayEventKind.Other)
        {
            stored.ProcessingError = $"Not applied: payment is {payment.Status}, event is {parsed.Kind}.";
            return NotificationOutcome.Ignored;
        }

        switch (parsed.Kind)
        {
            case GatewayEventKind.Paid:
                payment.Status = PaymentStatus.Paid;
                payment.PaidAt = now;
                await FulfilAsync(payment, now, cancellationToken).ConfigureAwait(false);
                break;
            case GatewayEventKind.Expired:
                payment.Status = PaymentStatus.Expired;
                break;
            default:
                payment.Status = PaymentStatus.Failed;
                payment.FailureReason = parsed.Reason;
                break;
        }

        return NotificationOutcome.Applied;
    }

    private async Task FulfilAsync(Payment payment, DateTime now, CancellationToken cancellationToken)
    {
        if (payment.ExamPurchaseId is { } purchaseId)
        {
            var purchase = await db.ExamPurchases.Include(p => p.Exam).SingleAsync(p => p.Id == purchaseId, cancellationToken).ConfigureAwait(false);
            var running = purchase.Status == ExamPurchaseStatus.Active && purchase.AccessExpiresAt > now ? purchase.AccessExpiresAt : null;
            var days = purchase.Exam!.AccessDays ?? throw new InvalidOperationException($"Exam {purchase.ExamId} has no access period.");
            purchase.Status = ExamPurchaseStatus.Active;
            purchase.PurchasedAt ??= now;
            purchase.PricePaidIdr = payment.AmountIdr;
            purchase.AccessExpiresAt = (running ?? now).AddDays(days);
            return;
        }

        var subscription = await db.Subscriptions.Include(s => s.PlanPrice).SingleAsync(s => s.Id == payment.SubscriptionId, cancellationToken).ConfigureAwait(false);
        var runningEnd = subscription.Status == SubscriptionStatus.Active && subscription.CurrentPeriodEnd > now ? subscription.CurrentPeriodEnd : null;
        if (runningEnd is null)
        {
            subscription.CurrentPeriodStart = now;
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = (runningEnd ?? now).AddMonths(subscription.PlanPrice!.DurationMonths);
        // A new period gets its own ending reminders (PAY-08).
        subscription.Reminder7dSentAt = null;
        subscription.Reminder1dSentAt = null;
    }
}
