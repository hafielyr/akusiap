using Akusiap.Domain.Access;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Payments;

/// <summary>One charge at the gateway for exactly one item: a subscription or an exam purchase (PAY-01).</summary>
public class Payment
{
    public long Id { get; set; }

    /// <summary>Readable reference for support, <c>LSI-yyyyMMdd-XXXX</c> (see <see cref="OrderNumbers"/>).</summary>
    public string OrderNumber { get; set; } = string.Empty;

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public long? SubscriptionId { get; set; }

    public Subscription? Subscription { get; set; }

    public long? ExamPurchaseId { get; set; }

    public ExamPurchase? ExamPurchase { get; set; }

    public long? PromoCodeId { get; set; }

    public PromoCode? PromoCode { get; set; }

    public long? SavedPaymentMethodId { get; set; }

    public SavedPaymentMethod? SavedPaymentMethod { get; set; }

    /// <summary>Rupiah, tax-inclusive (BR-20).</summary>
    public long AmountIdr { get; set; }

    public long DiscountIdr { get; set; }

    public long TaxIdr { get; set; }

    /// <summary>From settlement.</summary>
    public long? GatewayFeeIdr { get; set; }

    public string Gateway { get; set; } = PaymentGateways.Doku;

    public PaymentMethod Method { get; set; }

    /// <summary>BCA, BNI, BRI, MANDIRI, OVO, DANA …</summary>
    public string? Channel { get; set; }

    public string? VaNumber { get; set; }

    public string? QrString { get; set; }

    public string? DeeplinkUrl { get; set; }

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

    /// <summary>Pay-before deadline.</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>The gateway's order reference (DOKU <c>order.invoice_number</c>).</summary>
    public string InvoiceNumber { get; set; } = string.Empty;

    /// <summary>DOKU <c>Request-Id</c> of the checkout.</summary>
    public string? GatewayRequestId { get; set; }

    /// <summary>A success notification after Expired or Canceled (BR-23).</summary>
    public bool IsLatePayment { get; set; }

    public string? FailureReason { get; set; }

    public int RetryCount { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Required once Paid or Refunded.</summary>
    public DateTime? PaidAt { get; set; }
}

/// <summary>A refund of (part of) a payment.</summary>
public class Refund
{
    public long Id { get; set; }

    public long PaymentId { get; set; }

    public Payment? Payment { get; set; }

    public long AmountIdr { get; set; }

    public string Reason { get; set; } = string.Empty;

    public RefundStatus Status { get; set; } = RefundStatus.Requested;

    public bool IsAutoApproved { get; set; }

    /// <summary>Required once past Requested/Rejected.</summary>
    public RefundRoute? RefundRoute { get; set; }

    /// <summary>DOKU non-direct refund link; required while AwaitingDetails.</summary>
    public string? RefundLinkUrl { get; set; }

    public DateTime? RefundLinkExpiresAt { get; set; }

    public int AttemptCount { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>The admin; null when auto-approved.</summary>
    public Guid? DecidedByUserId { get; set; }

    public User? DecidedByUser { get; set; }

    public DateTime? DecidedAt { get; set; }

    public string? GatewayRefundId { get; set; }
}

/// <summary>A gateway notification, stored raw before processing; processed once per gateway event id (BR-26).</summary>
public class PaymentWebhookEvent
{
    public long Id { get; set; }

    public string Gateway { get; set; } = PaymentGateways.Doku;

    /// <summary>DOKU <c>Request-Id</c>.</summary>
    public string GatewayEventId { get; set; } = string.Empty;

    /// <summary>Null until matched to a payment.</summary>
    public long? PaymentId { get; set; }

    public Payment? Payment { get; set; }

    /// <summary>JSON (jsonb).</summary>
    public string Payload { get; set; } = "{}";

    public bool SignatureValid { get; set; }

    public DateTime ReceivedAt { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public string? ProcessingError { get; set; }
}

/// <summary>A discount code (table only until a later card uses it). Exactly one of percent or amount.</summary>
public class PromoCode
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    /// <summary>Above 0, at most 100, two decimals.</summary>
    public decimal? DiscountPercent { get; set; }

    public long? DiscountAmountIdr { get; set; }

    /// <summary>Null means unlimited.</summary>
    public int? MaxUses { get; set; }

    public int UsedCount { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>A card or direct-debit token for auto-renew (table only; auto-renew is Later). Never card data.</summary>
public class SavedPaymentMethod
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public string Gateway { get; set; } = PaymentGateways.Doku;

    public SavedPaymentMethodType Type { get; set; }

    /// <summary>For example "Visa ending 4417".</summary>
    public string DisplayLabel { get; set; } = string.Empty;

    public string GatewayToken { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
}
