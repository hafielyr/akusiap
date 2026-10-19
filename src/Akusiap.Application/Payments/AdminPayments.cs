using Akusiap.Application.Abstractions;
using Akusiap.Domain;
using Akusiap.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Payments;

/// <summary>Admin payment filters (all optional, combined). Dates are Jakarta days on the creation time.</summary>
public sealed record AdminPaymentFilter(
    PaymentStatus? Status = null,
    PaymentMethod? Method = null,
    DateOnly? From = null,
    DateOnly? To = null,
    string? User = null,
    int Page = 1);

public sealed record AdminPaymentRow(
    long Id,
    string OrderNumber,
    DateTime CreatedAt,
    DateTime? PaidAt,
    string UserEmail,
    string UserName,
    string Item,
    long AmountIdr,
    PaymentMethod Method,
    string? Channel,
    PaymentStatus Status,
    bool IsLatePayment,
    string? FailureReason,
    int RetryCount);

public sealed record AdminPaymentPage(IReadOnlyList<AdminPaymentRow> Items, int Page, int PageSize, int Total)
{
    public int PageCount => Math.Max(1, (Total + PageSize - 1) / PageSize);
}

public sealed record AdminWebhookEvent(string GatewayEventId, DateTime ReceivedAt, bool SignatureValid, DateTime? ProcessedAt, string? ProcessingError, string Payload);

public sealed record AdminRefund(long Id, long AmountIdr, string Reason, RefundStatus Status, DateTime RequestedAt, DateTime? DecidedAt);

public sealed record AdminPaymentDetail(AdminPaymentRow Payment, Guid UserId, string? VaNumber, DateTime? ExpiresAt, string InvoiceNumber, string? GatewayRequestId, IReadOnlyList<AdminWebhookEvent> Events, IReadOnlyList<AdminRefund> Refunds);

/// <summary>
/// Admin transactions (PAY-10, stories ADM-20, ADM-23, D-70): a filtered, paged list, the same rows for the CSV export,
/// and one payment with its gateway events and refunds. Payments are read only here; they are never deleted.
/// </summary>
public sealed class AdminPayments(IAppDbContext db)
{
    public const int PageSize = 25;
    public const int MaxExportRows = 10_000;

    public async Task<AdminPaymentPage> ListAsync(AdminPaymentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Filtered(filter);
        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var pageCount = Math.Max(1, (total + PageSize - 1) / PageSize);
        var page = Math.Clamp(filter.Page, 1, pageCount);
        var rows = await Rows(query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Skip((page - 1) * PageSize).Take(PageSize))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new AdminPaymentPage(rows, page, PageSize, total);
    }

    /// <summary>Every row matching the filter (newest first, at most <see cref="MaxExportRows"/>), for the CSV export.</summary>
    public async Task<IReadOnlyList<AdminPaymentRow>> ExportAsync(AdminPaymentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return await Rows(Filtered(filter).OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id).Take(MaxExportRows))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AdminPaymentDetail?> DetailAsync(long id, CancellationToken cancellationToken)
    {
        var row = await Rows(db.Payments.Where(p => p.Id == id)).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var extra = await db.Payments.Where(p => p.Id == id)
            .Select(p => new { p.UserId, p.VaNumber, p.ExpiresAt, p.InvoiceNumber, p.GatewayRequestId })
            .SingleAsync(cancellationToken).ConfigureAwait(false);
        var events = await db.PaymentWebhookEvents.Where(e => e.PaymentId == id).OrderBy(e => e.ReceivedAt).ThenBy(e => e.Id)
            .Select(e => new AdminWebhookEvent(e.GatewayEventId, e.ReceivedAt, e.SignatureValid, e.ProcessedAt, e.ProcessingError, e.Payload))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var refunds = await db.Refunds.Where(r => r.PaymentId == id).OrderBy(r => r.RequestedAt)
            .Select(r => new AdminRefund(r.Id, r.AmountIdr, r.Reason, r.Status, r.RequestedAt, r.DecidedAt))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new AdminPaymentDetail(row, extra.UserId, extra.VaNumber, extra.ExpiresAt, extra.InvoiceNumber, extra.GatewayRequestId, events, refunds);
    }

    private IQueryable<Payment> Filtered(AdminPaymentFilter filter)
    {
        var query = db.Payments.AsQueryable();
        if (filter.Status is { } status)
        {
            query = query.Where(p => p.Status == status);
        }

        if (filter.Method is { } method)
        {
            query = query.Where(p => p.Method == method);
        }

        if (filter.From is { } from)
        {
            var start = JakartaTime.StartOfDayUtc(from);
            query = query.Where(p => p.CreatedAt >= start);
        }

        if (filter.To is { } to)
        {
            var end = JakartaTime.StartOfDayUtc(to.AddDays(1));
            query = query.Where(p => p.CreatedAt < end);
        }

        if (!string.IsNullOrWhiteSpace(filter.User))
        {
#pragma warning disable CA1304, CA1311, CA1862 // ToLower() is translated to SQL lower().
            var term = filter.User.Trim().ToLower();
            query = query.Where(p => p.User!.Email!.ToLower().Contains(term) || p.User.DisplayName.ToLower().Contains(term) || p.OrderNumber.ToLower().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return query;
    }

    private static IQueryable<AdminPaymentRow> Rows(IQueryable<Payment> payments) => payments.Select(p => new AdminPaymentRow(
        p.Id,
        p.OrderNumber,
        p.CreatedAt,
        p.PaidAt,
        p.User!.Email!,
        p.User.DisplayName,
        p.ExamPurchase != null ? p.ExamPurchase.Exam!.Title : p.Subscription!.Plan!.Name + " " + p.Subscription.PlanPrice!.DurationMonths + " bln",
        p.AmountIdr,
        p.Method,
        p.Channel,
        p.Status,
        p.IsLatePayment,
        p.FailureReason,
        p.RetryCount));
}
