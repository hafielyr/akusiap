namespace Akusiap.Domain.Access;

public enum PlanCode
{
    Free,
    Learner,
    Teacher,
    Combined,
}

public enum SubscriptionStatus
{
    PendingPayment,
    Active,
    Expired,
    Canceled,
}

public enum RenewalMode
{
    Manual,
    AutoDebit,
}

public enum ExamPurchaseStatus
{
    PendingPayment,
    Active,
    Expired,
    Refunded,
}
