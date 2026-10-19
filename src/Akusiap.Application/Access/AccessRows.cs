using Akusiap.Domain;
using Akusiap.Domain.Access;

namespace Akusiap.Application.Access;

/// <summary>How a purchase or subscription row reads for people: Active rows whose end passed count as Ended.</summary>
public enum AccessState
{
    Active,
    Ended,
    AwaitingPayment,
    Abandoned,
    Refunded,
}

public sealed record PurchaseRow(
    long Id,
    long ExamId,
    string ExamTitle,
    AccessState State,
    long PricePaidIdr,
    DateTime? StartedAt,
    DateTime? EndsAt,
    int? DaysLeft);

public sealed record SubscriptionRow(
    long Id,
    long PlanId,
    string PlanName,
    bool IncludesAllExams,
    AccessState State,
    DateTime? StartedAt,
    DateTime? EndsAt,
    int? DaysLeft);

public static class AccessStates
{
    public static AccessState Of(ExamPurchaseStatus status, DateTime? end, DateTime now) => status switch
    {
        ExamPurchaseStatus.Active => end > now ? AccessState.Active : AccessState.Ended,
        ExamPurchaseStatus.Expired => AccessState.Ended,
        ExamPurchaseStatus.AwaitingPayment => AccessState.AwaitingPayment,
        ExamPurchaseStatus.Abandoned => AccessState.Abandoned,
        _ => AccessState.Refunded,
    };

    public static AccessState Of(SubscriptionStatus status, DateTime? end, DateTime now) => status switch
    {
        SubscriptionStatus.Active => end > now ? AccessState.Active : AccessState.Ended,
        SubscriptionStatus.Expired => AccessState.Ended,
        SubscriptionStatus.AwaitingPayment => AccessState.AwaitingPayment,
        SubscriptionStatus.Abandoned => AccessState.Abandoned,
        _ => AccessState.Refunded,
    };

    /// <summary>Jakarta days left while the row is active; null otherwise.</summary>
    public static int? DaysLeft(AccessState state, DateTime? end, DateTime now) =>
        state == AccessState.Active && end is { } e ? JakartaTime.DaysLeft(e, now) : null;
}
