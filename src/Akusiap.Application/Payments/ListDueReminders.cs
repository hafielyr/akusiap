using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Payments;

public sealed record DueReminder(long SubscriptionId, Guid UserId, string Email, string DisplayName, string PlanName, int DurationMonths, long PlanPriceId, DateTime PeriodEnd, RenewalReminder Kind);

/// <summary>
/// Active packages that need a 7-day or 1-day ending reminder now (PAY-08, story P-07, <see cref="RenewalReminderPolicy"/>),
/// and marking one as sent. Exam purchases have no reminders (schema.sql has guards on subscriptions only, D-77).
/// </summary>
public sealed class ListDueReminders(IAppDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<DueReminder>> HandleAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var horizon = now.AddDays(9);
        var rows = await db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd > now && s.CurrentPeriodEnd <= horizon)
            .Where(s => s.Reminder7dSentAt == null || s.Reminder1dSentAt == null)
            .Where(s => s.User!.Email != null && s.User.IsActive)
            .OrderBy(s => s.CurrentPeriodEnd)
            .Select(s => new
            {
                s.Id,
                s.UserId,
                Email = s.User!.Email!,
                s.User.DisplayName,
                PlanName = s.Plan!.Name,
                s.PlanPrice!.DurationMonths,
                s.PlanPriceId,
                End = s.CurrentPeriodEnd!.Value,
                s.Reminder7dSentAt,
                s.Reminder1dSentAt,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows
            .Select(r => new DueReminder(r.Id, r.UserId, r.Email, r.DisplayName, r.PlanName, r.DurationMonths, r.PlanPriceId, r.End, RenewalReminderPolicy.Due(r.End, now, r.Reminder7dSentAt, r.Reminder1dSentAt)))
            .Where(r => r.Kind != RenewalReminder.None)];
    }

    /// <summary>Records the reminder as sent; the 1-day one also covers the 7-day one, so it never follows it.</summary>
    public async Task MarkSentAsync(long subscriptionId, RenewalReminder kind, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var subscription = await db.Subscriptions.SingleAsync(s => s.Id == subscriptionId, cancellationToken).ConfigureAwait(false);
        subscription.Reminder7dSentAt ??= now;
        if (kind == RenewalReminder.OneDay)
        {
            subscription.Reminder1dSentAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
