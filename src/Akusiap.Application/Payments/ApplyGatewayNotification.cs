using Akusiap.Application.Abstractions;
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

    /// <summary>Stored and applied to the payment's status.</summary>
    Applied,
}

/// <summary>
/// Stores a gateway notification and moves a Pending payment to Paid, Expired or Failed (PAY-01, D-52). Every
/// readable notification is kept in <c>payment_webhook_events</c> with its signature check; invalid ones are never
/// applied. PAY-06 adds the public webhook endpoint, late payments and fulfilling the purchase or subscription.
/// </summary>
public sealed class ApplyGatewayNotification(IAppDbContext db, IPaymentGateway gateway, IClock clock)
{
    public async Task<(NotificationOutcome Outcome, Payment? Payment)> HandleAsync(GatewayNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var parsed = gateway.ParseNotification(notification);
        if (parsed is null)
        {
            return (NotificationOutcome.Unreadable, null);
        }

        if (await db.PaymentWebhookEvents.AnyAsync(e => e.Gateway == gateway.Name && e.GatewayEventId == parsed.EventId, cancellationToken).ConfigureAwait(false))
        {
            return (NotificationOutcome.Duplicate, null);
        }

        var now = clock.UtcNow;
        var signatureValid = gateway.VerifySignature(notification);
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.InvoiceNumber == parsed.InvoiceNumber, cancellationToken).ConfigureAwait(false);
        var stored = new PaymentWebhookEvent
        {
            Gateway = gateway.Name,
            GatewayEventId = parsed.EventId,
            PaymentId = payment?.Id,
            Payload = notification.Body,
            SignatureValid = signatureValid,
            ReceivedAt = now,
        };
        db.PaymentWebhookEvents.Add(stored);

        var outcome = Apply(parsed, payment, signatureValid, now, stored);
        try
        {
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

        return (outcome, payment);
    }

    private static NotificationOutcome Apply(GatewayEvent parsed, Payment? payment, bool signatureValid, DateTime now, PaymentWebhookEvent stored)
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
}
