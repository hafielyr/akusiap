using Akusiap.Domain.Users;

namespace Akusiap.Domain.Access;

/// <summary>A package bought by a user. At most one live (not Abandoned or Refunded) row per user and plan; renewals extend it.</summary>
public class Subscription
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public long PlanId { get; set; }

    public Plan? Plan { get; set; }

    /// <summary>The package bought; must belong to <see cref="PlanId"/>.</summary>
    public long PlanPriceId { get; set; }

    public PlanPrice? PlanPrice { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.AwaitingPayment;

    /// <summary>Set when paid. Active requires both, with end after start.</summary>
    public DateTime? CurrentPeriodStart { get; set; }

    public DateTime? CurrentPeriodEnd { get; set; }

    public RenewalMode RenewalMode { get; set; } = RenewalMode.Manual;

    /// <summary>Required for AutoDebit. The foreign key comes with the saved_payment_methods table in PAY-01.</summary>
    public long? SavedPaymentMethodId { get; set; }

    /// <summary>Failed auto-debit only.</summary>
    public DateTime? GraceUntil { get; set; }

    public DateTime? Reminder7dSentAt { get; set; }

    public DateTime? Reminder1dSentAt { get; set; }

    /// <summary>Auto-debit only.</summary>
    public string? GatewaySubscriptionId { get; set; }

    public DateTime CreatedAt { get; set; }
}
