namespace Akusiap.Application.Abstractions;

/// <summary>Background work after a payment (PAY-07). Hangfire in the app; run at once when jobs are off (tests).</summary>
public interface IPaymentJobs
{
    /// <summary>Queues the receipt email for a payment that was just paid.</summary>
    Task QueueReceiptAsync(long paymentId, CancellationToken cancellationToken);
}
