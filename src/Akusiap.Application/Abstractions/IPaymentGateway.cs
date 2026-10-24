using Akusiap.Domain.Payments;

namespace Akusiap.Application.Abstractions;

/// <summary>What to charge. <paramref name="Channel"/> is the bank or wallet (BCA, OVO …) where the method needs one.</summary>
public sealed record ChargeRequest(
    string InvoiceNumber,
    long AmountIdr,
    PaymentMethod Method,
    string? Channel,
    string ItemName,
    string CustomerName,
    string CustomerEmail);

/// <summary>How the learner pays: the fields that apply to the method, and the pay-before deadline set by the gateway.</summary>
public sealed record ChargeInstructions(string? GatewayRequestId, string? QrString, string? VaNumber, string? DeeplinkUrl, DateTime ExpiresAt);

/// <summary>An HTTP notification from the gateway, as received.</summary>
public sealed record GatewayNotification(IReadOnlyDictionary<string, string> Headers, string Body);

public enum GatewayEventKind
{
    Paid,
    Expired,
    Failed,

    /// <summary>Anything the app doesn't act on.</summary>
    Other,
}

/// <param name="EventId">Unique per notification (DOKU <c>Request-Id</c>); the duplicate guard (BR-26).</param>
public sealed record GatewayEvent(string EventId, string InvoiceNumber, GatewayEventKind Kind, long? AmountIdr, string? Reason);

/// <summary>A status the app asked the gateway for itself (not a notification); <paramref name="Body"/> is the answer as received.</summary>
public sealed record GatewayStatusCheck(GatewayEvent Event, string Body);

public sealed record GatewayRefundRequest(string InvoiceNumber, long AmountIdr, string Reason);

/// <param name="RefundLinkUrl">Set when the payer must give details first (DOKU non-direct refunds).</param>
public sealed record GatewayRefundResult(bool Accepted, string? GatewayRefundId, string? RefundLinkUrl, string? Error);

/// <summary>
/// The payment gateway (PAY-01). DOKU implements it in PAY-04; <c>FakePaymentGateway</c> is used in Development and tests.
/// </summary>
public interface IPaymentGateway
{
    /// <summary>The value stored in <c>payments.gateway</c>.</summary>
    string Name { get; }

    Task<ChargeInstructions> CreateChargeAsync(ChargeRequest request, CancellationToken cancellationToken);

    /// <summary>Whether the notification really comes from the gateway. Never trust a notification that fails this.</summary>
    bool VerifySignature(GatewayNotification notification);

    /// <summary>The event in a notification, or null when the body can't be read.</summary>
    GatewayEvent? ParseNotification(GatewayNotification notification);

    /// <summary>
    /// Asks the gateway for an invoice's current status: the fallback when its notification doesn't arrive (a missed
    /// webhook, or a dev machine the gateway can't reach). Null when the gateway doesn't know the invoice.
    /// </summary>
    Task<GatewayStatusCheck?> CheckStatusAsync(string invoiceNumber, CancellationToken cancellationToken);

    Task<GatewayRefundResult> RefundAsync(GatewayRefundRequest request, CancellationToken cancellationToken);
}
