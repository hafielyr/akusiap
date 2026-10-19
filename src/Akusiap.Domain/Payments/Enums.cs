namespace Akusiap.Domain.Payments;

/// <summary>The payment gateway names stored in <c>gateway</c> columns. Only DOKU for now (D-10).</summary>
public static class PaymentGateways
{
    public const string Doku = "DOKU";
}

/// <summary>How the learner pays (ID-10 to ID-12). Names are stored as they are.</summary>
public enum PaymentMethod
{
    QRIS,
    VirtualAccount,
    EWallet,
    CreditCard,
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Expired,
    Canceled,
    Failed,
    Refunded,
}

public enum RefundStatus
{
    Requested,
    Approved,
    Rejected,
    AwaitingDetails,
    SentToDoku,
    Completed,
    Failed,
}

public enum RefundRoute
{
    DirectToPayerApp,
    RefundLink,
}

public enum SavedPaymentMethodType
{
    CreditCard,
    DirectDebit,
}
