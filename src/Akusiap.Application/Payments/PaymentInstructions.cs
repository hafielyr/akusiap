using Akusiap.Application.Abstractions;
using Akusiap.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Payments;

/// <summary>How a payment reads for the learner. A Pending payment past its deadline already counts as Expired.</summary>
public enum PaymentDisplayState
{
    Pending,
    Paid,
    Expired,
    Failed,
}

/// <param name="ExamId">The exam bought, for the "start practice" link once paid; null for packages.</param>
/// <param name="SecondsLeft">Until the pay-before deadline while Pending; null otherwise.</param>
public sealed record PaymentView(
    long Id,
    string OrderNumber,
    string ItemTitle,
    int? DurationMonths,
    long? ExamId,
    PaymentMethod Method,
    string? Channel,
    long AmountIdr,
    string? VaNumber,
    string? QrString,
    string? DeeplinkUrl,
    DateTime? ExpiresAt,
    PaymentDisplayState State,
    int? SecondsLeft);

/// <summary>
/// The learner's own payments (PAY-05, stories ID-10 to ID-13): one payment's instructions by order number, and the list
/// of payments still waiting. Other users' payments are never returned.
/// </summary>
public sealed class GetPayments(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public async Task<PaymentView?> ByOrderNumberAsync(string orderNumber, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null)
        {
            return null;
        }

        var views = await QueryAsync(db.Payments.Where(p => p.OrderNumber == orderNumber && p.UserId == userId), cancellationToken).ConfigureAwait(false);
        return views.SingleOrDefault();
    }

    /// <summary>Payments still waiting (Pending and before their deadline), newest first.</summary>
    public async Task<IReadOnlyList<PaymentView>> PendingAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("Pending payments need a signed-in user.");
        var views = await QueryAsync(db.Payments.Where(p => p.UserId == userId && p.Status == PaymentStatus.Pending), cancellationToken).ConfigureAwait(false);
        return [.. views.Where(v => v.State == PaymentDisplayState.Pending).OrderByDescending(v => v.Id)];
    }

    public static PaymentDisplayState StateOf(PaymentStatus status, DateTime? expiresAt, DateTime now) => status switch
    {
        PaymentStatus.Paid or PaymentStatus.Refunded => PaymentDisplayState.Paid,
        PaymentStatus.Expired or PaymentStatus.Canceled => PaymentDisplayState.Expired,
        PaymentStatus.Failed => PaymentDisplayState.Failed,
        _ => expiresAt is { } deadline && deadline <= now ? PaymentDisplayState.Expired : PaymentDisplayState.Pending,
    };

    private async Task<List<PaymentView>> QueryAsync(IQueryable<Payment> payments, CancellationToken cancellationToken)
    {
        var rows = await payments
            .Select(p => new
            {
                p.Id,
                p.OrderNumber,
                ExamTitle = p.ExamPurchase == null ? null : p.ExamPurchase.Exam!.Title,
                ExamId = p.ExamPurchase == null ? (long?)null : p.ExamPurchase.ExamId,
                PlanName = p.Subscription == null ? null : p.Subscription.Plan!.Name,
                Months = p.Subscription == null ? (int?)null : p.Subscription.PlanPrice!.DurationMonths,
                p.Method,
                p.Channel,
                p.AmountIdr,
                p.VaNumber,
                p.QrString,
                p.DeeplinkUrl,
                p.ExpiresAt,
                p.Status,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var now = clock.UtcNow;
        return [.. rows.Select(r =>
        {
            var state = StateOf(r.Status, r.ExpiresAt, now);
            int? secondsLeft = state == PaymentDisplayState.Pending && r.ExpiresAt is { } deadline
                ? (int)Math.Max(0, Math.Floor((deadline - now).TotalSeconds))
                : null;
            return new PaymentView(
                r.Id, r.OrderNumber, r.ExamTitle ?? r.PlanName ?? string.Empty, r.Months, r.ExamId, r.Method, r.Channel, r.AmountIdr,
                r.VaNumber, r.QrString, r.DeeplinkUrl, r.ExpiresAt, state, secondsLeft);
        })];
    }
}
