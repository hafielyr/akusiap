using Akusiap.Domain.Users;

namespace Akusiap.Domain.Access;

public class Subscription
{
    public int SubscriptionId { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public int PlanId { get; set; }

    public Plan? Plan { get; set; }

    /// <summary>The package bought.</summary>
    public int PlanPriceId { get; set; }

    public PlanPrice? PlanPrice { get; set; }

    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.PendingPayment;

    public DateTime CurrentPeriodStart { get; set; }

    public DateTime CurrentPeriodEnd { get; set; }

    public RenewalMode RenewalMode { get; set; } = RenewalMode.Manual;

    /// <summary>AutoDebit only. Its foreign key comes with the SavedPaymentMethod table in PAY-01 (D-22).</summary>
    public int? SavedPaymentMethodId { get; set; }

    /// <summary>Set after a failed auto-debit.</summary>
    public DateTime? GraceUntil { get; set; }

    public DateTime? Reminder7dSentAt { get; set; }

    public DateTime? Reminder1dSentAt { get; set; }

    /// <summary>AutoDebit only.</summary>
    public string? GatewaySubscriptionId { get; set; }

    public DateTime CreatedAt { get; set; }
}
