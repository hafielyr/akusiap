namespace Akusiap.Domain.Access;

public enum PlanCode
{
    Free,
    Learner,
    Teacher,
    Combined,
}

/// <summary>Abandoned: the checkout was never paid. Refunded: money returned, access ended.</summary>
public enum SubscriptionStatus
{
    AwaitingPayment,
    Active,
    Expired,
    Abandoned,
    Refunded,
}

public enum RenewalMode
{
    Manual,
    AutoDebit,
}

/// <summary>Same lifecycle as <see cref="SubscriptionStatus"/>.</summary>
public enum ExamPurchaseStatus
{
    AwaitingPayment,
    Active,
    Expired,
    Abandoned,
    Refunded,
}
