using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain;
using Akusiap.Domain.Access;
using Akusiap.Domain.Auditing;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Access;

public enum AccessKind
{
    Purchase,
    Subscription,
}

/// <param name="Until">A custom last day (Jakarta); null uses the exam's access days.</param>
public sealed record GrantExamRequest(Guid UserId, long ExamId, DateOnly? Until);

public sealed record GrantPlanRequest(Guid UserId, long PlanId, int Days);

/// <param name="Until">The new last day (Jakarta). Re-activates access that ended.</param>
public sealed record ChangeAccessEndRequest(Guid UserId, AccessKind Kind, long Id, DateOnly Until);

public sealed record EndAccessRequest(Guid UserId, AccessKind Kind, long Id);

/// <summary>
/// Admin grants, extends and ends access by hand (ENT-03, story ADM-18). Grants reuse the user's live row for the exam or
/// plan (one per user, BR-08/BR-10) and are free (PricePaidIdr 0). Every change writes an <see cref="AuditLog"/> row with
/// the admin, the row and its status and end before and after. Returns the row's new end (UTC).
/// </summary>
public sealed class ManageAccess(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public const string ExamIsFree = "Admin.Access.ExamIsFree";
    public const string NeedsDate = "Admin.Access.NeedsDate";
    public const string DateInPast = "Admin.Access.DateInPast";
    public const string PlanNotGrantable = "Admin.Access.PlanNotGrantable";
    public const string PlanHasNoPrice = "Admin.Access.PlanHasNoPrice";
    public const string InvalidDays = "Admin.Access.InvalidDays";
    public const string NotChangeable = "Admin.Access.NotChangeable";
    public const string NotActive = "Admin.Access.NotActive";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<DateTime>> GrantExamAsync(GrantExamRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var exam = await db.Exams
            .Where(e => e.Id == request.ExamId && e.OwnerKind == OwnerKind.App)
            .Select(e => new { e.Id, e.IsFree, e.AccessDays })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (exam is null || !await UserExistsAsync(request.UserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<DateTime>(Problem.NotFound);
        }

        if (exam.IsFree)
        {
            return Result.Fail<DateTime>(new Problem(ExamIsFree));
        }

        var purchase = await db.ExamPurchases
            .Where(p => p.UserId == request.UserId && p.ExamId == exam.Id)
            .Where(p => p.Status != ExamPurchaseStatus.Abandoned && p.Status != ExamPurchaseStatus.Refunded)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var running = purchase is { Status: ExamPurchaseStatus.Active } ? purchase.AccessExpiresAt : null;
        var end = AccessGrants.ExamGrantEnd(now, running, exam.AccessDays, request.Until);
        if (end is null)
        {
            return Result.Fail<DateTime>(new Problem(NeedsDate, "Until"));
        }

        if (!AccessGrants.IsInFuture(end.Value, now))
        {
            return Result.Fail<DateTime>(new Problem(DateInPast, "Until"));
        }

        var before = purchase is null ? null : Snapshot(purchase.Status.ToString(), purchase.AccessExpiresAt);
        if (purchase is null)
        {
            purchase = new ExamPurchase { UserId = request.UserId, ExamId = exam.Id, PricePaidIdr = 0 };
            db.ExamPurchases.Add(purchase);
        }
        else if (purchase.Status == ExamPurchaseStatus.AwaitingPayment)
        {
            // The unpaid checkout becomes the free grant.
            purchase.PricePaidIdr = 0;
        }

        purchase.Status = ExamPurchaseStatus.Active;
        purchase.PurchasedAt ??= now;
        purchase.AccessExpiresAt = end;
        await SaveWithAuditAsync(
            "Access.GrantExam",
            nameof(ExamPurchase),
            () => purchase.Id,
            request.UserId,
            before,
            Snapshot(purchase.Status.ToString(), end),
            new { examId = exam.Id },
            cancellationToken).ConfigureAwait(false);
        return Result.Ok(end.Value);
    }

    public async Task<Result<DateTime>> GrantPlanAsync(GrantPlanRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == request.PlanId, cancellationToken).ConfigureAwait(false);
        if (plan is null || !await UserExistsAsync(request.UserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<DateTime>(Problem.NotFound);
        }

        if (!plan.IsActive || plan.Code == PlanCode.Free)
        {
            return Result.Fail<DateTime>(new Problem(PlanNotGrantable));
        }

        if (!AccessGrants.IsValidPlanDays(request.Days))
        {
            return Result.Fail<DateTime>(new Problem(InvalidDays, "Days", AccessGrants.MaxPlanDays));
        }

        var subscription = await db.Subscriptions
            .Where(s => s.UserId == request.UserId && s.PlanId == plan.Id)
            .Where(s => s.Status != SubscriptionStatus.Abandoned && s.Status != SubscriptionStatus.Refunded)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (subscription is null)
        {
            // subscriptions.plan_price_id is required; link the plan's shortest package (nothing is charged).
            var priceId = await db.PlanPrices
                .Where(p => p.PlanId == plan.Id)
                .OrderByDescending(p => p.IsActive)
                .ThenBy(p => p.DurationMonths)
                .Select(p => (long?)p.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (priceId is null)
            {
                return Result.Fail<DateTime>(new Problem(PlanHasNoPrice, null, plan.Name));
            }

            subscription = new Subscription { UserId = request.UserId, PlanId = plan.Id, PlanPriceId = priceId.Value, CreatedAt = now };
            db.Subscriptions.Add(subscription);
        }

        var before = subscription.Id == 0 ? null : Snapshot(subscription.Status.ToString(), subscription.CurrentPeriodEnd);
        var isRunning = subscription.Status == SubscriptionStatus.Active && subscription.CurrentPeriodEnd > now;
        var end = AccessGrants.PlanGrantEnd(now, isRunning ? subscription.CurrentPeriodEnd : null, request.Days);
        if (!isRunning)
        {
            subscription.CurrentPeriodStart = now;
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = end;
        await SaveWithAuditAsync(
            "Access.GrantPlan",
            nameof(Subscription),
            () => subscription.Id,
            request.UserId,
            before,
            Snapshot(subscription.Status.ToString(), end),
            new { planId = plan.Id, days = request.Days },
            cancellationToken).ConfigureAwait(false);
        return Result.Ok(end);
    }

    public async Task<Result<DateTime>> ChangeEndAsync(ChangeAccessEndRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var end = JakartaTime.EndOfDayUtc(request.Until);
        if (request.Kind == AccessKind.Purchase)
        {
            var purchase = await FindPurchaseAsync(request.UserId, request.Id, cancellationToken).ConfigureAwait(false);
            if (purchase is null)
            {
                return Result.Fail<DateTime>(Problem.NotFound);
            }

            if (purchase.Status is not (ExamPurchaseStatus.Active or ExamPurchaseStatus.Expired))
            {
                return Result.Fail<DateTime>(new Problem(NotChangeable));
            }

            if (!AccessGrants.IsInFuture(end, now))
            {
                return Result.Fail<DateTime>(new Problem(DateInPast, "Until"));
            }

            var before = Snapshot(purchase.Status.ToString(), purchase.AccessExpiresAt);
            purchase.Status = ExamPurchaseStatus.Active;
            purchase.AccessExpiresAt = end;
            await SaveWithAuditAsync("Access.ChangeEnd", nameof(ExamPurchase), () => purchase.Id, request.UserId, before, Snapshot(purchase.Status.ToString(), end), null, cancellationToken).ConfigureAwait(false);
            return Result.Ok(end);
        }

        var subscription = await FindSubscriptionAsync(request.UserId, request.Id, cancellationToken).ConfigureAwait(false);
        if (subscription is null)
        {
            return Result.Fail<DateTime>(Problem.NotFound);
        }

        if (subscription.Status is not (SubscriptionStatus.Active or SubscriptionStatus.Expired))
        {
            return Result.Fail<DateTime>(new Problem(NotChangeable));
        }

        if (!AccessGrants.IsInFuture(end, now))
        {
            return Result.Fail<DateTime>(new Problem(DateInPast, "Until"));
        }

        var subscriptionBefore = Snapshot(subscription.Status.ToString(), subscription.CurrentPeriodEnd);
        if (subscription.Status != SubscriptionStatus.Active || subscription.CurrentPeriodStart is null || subscription.CurrentPeriodEnd <= now)
        {
            subscription.CurrentPeriodStart = now;
        }

        subscription.Status = SubscriptionStatus.Active;
        subscription.CurrentPeriodEnd = end;
        await SaveWithAuditAsync("Access.ChangeEnd", nameof(Subscription), () => subscription.Id, request.UserId, subscriptionBefore, Snapshot(subscription.Status.ToString(), end), null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(end);
    }

    public async Task<Result<DateTime>> EndAsync(EndAccessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        if (request.Kind == AccessKind.Purchase)
        {
            var purchase = await FindPurchaseAsync(request.UserId, request.Id, cancellationToken).ConfigureAwait(false);
            if (purchase is null)
            {
                return Result.Fail<DateTime>(Problem.NotFound);
            }

            if (purchase.Status != ExamPurchaseStatus.Active)
            {
                return Result.Fail<DateTime>(new Problem(NotActive));
            }

            var before = Snapshot(purchase.Status.ToString(), purchase.AccessExpiresAt);
            var end = AccessGrants.EndNow(now, purchase.AccessExpiresAt);
            purchase.Status = ExamPurchaseStatus.Expired;
            purchase.AccessExpiresAt = end;
            await SaveWithAuditAsync("Access.End", nameof(ExamPurchase), () => purchase.Id, request.UserId, before, Snapshot(purchase.Status.ToString(), end), null, cancellationToken).ConfigureAwait(false);
            return Result.Ok(end);
        }

        var subscription = await FindSubscriptionAsync(request.UserId, request.Id, cancellationToken).ConfigureAwait(false);
        if (subscription is null)
        {
            return Result.Fail<DateTime>(Problem.NotFound);
        }

        if (subscription.Status != SubscriptionStatus.Active)
        {
            return Result.Fail<DateTime>(new Problem(NotActive));
        }

        var subscriptionBefore = Snapshot(subscription.Status.ToString(), subscription.CurrentPeriodEnd);
        var subscriptionEnd = AccessGrants.EndNow(now, subscription.CurrentPeriodEnd);
        subscription.Status = SubscriptionStatus.Expired;
        subscription.CurrentPeriodEnd = subscriptionEnd;
        await SaveWithAuditAsync("Access.End", nameof(Subscription), () => subscription.Id, request.UserId, subscriptionBefore, Snapshot(subscription.Status.ToString(), subscriptionEnd), null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(subscriptionEnd);
    }

    private Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id == userId, cancellationToken);

    private Task<ExamPurchase?> FindPurchaseAsync(Guid userId, long id, CancellationToken cancellationToken) =>
        db.ExamPurchases.SingleOrDefaultAsync(p => p.Id == id && p.UserId == userId, cancellationToken);

    private Task<Subscription?> FindSubscriptionAsync(Guid userId, long id, CancellationToken cancellationToken) =>
        db.Subscriptions.SingleOrDefaultAsync(s => s.Id == id && s.UserId == userId, cancellationToken);

    private static object Snapshot(string status, DateTime? end) => new { status, end };

    /// <summary>Saves the row, then its audit entry (which needs the row's id), in one transaction.</summary>
    private async Task SaveWithAuditAsync(
        string action, string entityName, Func<long> entityId, Guid userId, object? before, object after, object? extra, CancellationToken cancellationToken)
    {
        var actor = currentUser.UserId ?? throw new InvalidOperationException("Access changes need a signed-in admin.");
        await using var transaction = await db.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actor,
            Action = action,
            EntityName = entityName,
            EntityId = entityId().ToString(System.Globalization.CultureInfo.InvariantCulture),
            Changes = JsonSerializer.Serialize(new { userId, before, after, extra }, Json),
            CreatedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
