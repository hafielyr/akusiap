using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Access;

public sealed record AccessUser(Guid Id, string Email, string DisplayName, bool IsActive);

public sealed record UserAccess(AccessUser User, IReadOnlyList<PurchaseRow> Purchases, IReadOnlyList<SubscriptionRow> Subscriptions);

/// <summary>A paid app exam an admin can grant. Exams without access days need a custom end date.</summary>
public sealed record GrantableExam(long Id, string Title, int? AccessDays, ExamStatus Status);

/// <summary>A paid plan an admin can grant. A grant needs one of the plan's prices to link to (until PAY-02 adds them, none).</summary>
public sealed record GrantablePlan(long Id, string Name, bool HasPrice);

public sealed record GrantOptions(IReadOnlyList<GrantableExam> Exams, IReadOnlyList<GrantablePlan> Plans);

/// <summary>
/// The admin access page's data (ENT-03): one user found by exact email, with every exam purchase and subscription,
/// newest end first, and what can be granted.
/// </summary>
public sealed class GetUserAccess(IAppDbContext db, IClock clock)
{
    public async Task<UserAccess?> FindByEmailAsync(string? email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var normalized = email.Trim().ToUpperInvariant();
        var user = await db.Users
            .Where(u => u.NormalizedEmail == normalized)
            .Select(u => new AccessUser(u.Id, u.Email!, u.DisplayName, u.IsActive))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return user is null ? null : await ForUserAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserAccess?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new AccessUser(u.Id, u.Email!, u.DisplayName, u.IsActive))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return user is null ? null : await ForUserAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<GrantOptions> OptionsAsync(CancellationToken cancellationToken)
    {
        var exams = await db.Exams
            .Where(e => e.OwnerKind == OwnerKind.App && !e.IsFree && e.Status != ExamStatus.TakenDown)
            .OrderBy(e => e.Title)
            .Select(e => new GrantableExam(e.Id, e.Title, e.AccessDays, e.Status))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var plans = await db.Plans
            .Where(p => p.IsActive && p.Code != PlanCode.Free)
            .OrderBy(p => p.Id)
            .Select(p => new GrantablePlan(p.Id, p.Name, p.Prices.Any()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return new GrantOptions(exams, plans);
    }

    private async Task<UserAccess> ForUserAsync(AccessUser user, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var purchases = await db.ExamPurchases
            .Where(p => p.UserId == user.Id)
            .Select(p => new { p.Id, p.ExamId, ExamTitle = p.Exam!.Title, p.Status, p.PricePaidIdr, p.PurchasedAt, p.AccessExpiresAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var subscriptions = await db.Subscriptions
            .Where(s => s.UserId == user.Id)
            .Select(s => new { s.Id, s.PlanId, PlanName = s.Plan!.Name, s.Plan.IncludesAllExams, s.Status, s.CurrentPeriodStart, s.CurrentPeriodEnd })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new UserAccess(
            user,
            [.. purchases
                .Select(p =>
                {
                    var state = AccessStates.Of(p.Status, p.AccessExpiresAt, now);
                    return new PurchaseRow(p.Id, p.ExamId, p.ExamTitle, state, p.PricePaidIdr, p.PurchasedAt, p.AccessExpiresAt, AccessStates.DaysLeft(state, p.AccessExpiresAt, now));
                })
                .OrderByDescending(p => p.EndsAt ?? DateTime.MaxValue)
                .ThenByDescending(p => p.Id)],
            [.. subscriptions
                .Select(s =>
                {
                    var state = AccessStates.Of(s.Status, s.CurrentPeriodEnd, now);
                    return new SubscriptionRow(s.Id, s.PlanId, s.PlanName, s.IncludesAllExams, state, s.CurrentPeriodStart, s.CurrentPeriodEnd, AccessStates.DaysLeft(state, s.CurrentPeriodEnd, now));
                })
                .OrderByDescending(s => s.EndsAt ?? DateTime.MaxValue)
                .ThenByDescending(s => s.Id)]);
    }
}
