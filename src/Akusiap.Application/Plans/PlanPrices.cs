using System.Text.Json;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Access;
using Akusiap.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Plans;

/// <summary>One package of a plan: a duration with its price, or no price yet.</summary>
public sealed record PlanPackage(int DurationMonths, long? PriceIdr, bool IsActive, int SubscriptionCount)
{
    public bool InUse => SubscriptionCount > 0;
}

public sealed record PlanPricing(long PlanId, PlanCode Code, string Name, bool IncludesAllExams, IReadOnlyList<PlanPackage> Packages);

/// <summary>What the admin typed for one duration. A null price means "no package for this duration".</summary>
public sealed record PackageInput(int DurationMonths, long? PriceIdr, bool IsActive);

public sealed record SavePlanPricesRequest(long PlanId, IReadOnlyList<PackageInput> Packages);

/// <summary>The paid plans (Free has no prices) with a slot for each allowed duration: 1, 3, 6 and 12 months (PAY-02).</summary>
public sealed class ListPlanPricing(IAppDbContext db)
{
    public async Task<IReadOnlyList<PlanPricing>> HandleAsync(CancellationToken cancellationToken)
    {
        var plans = await db.Plans
            .Where(p => p.Code != PlanCode.Free)
            .Select(p => new
            {
                p.Id,
                p.Code,
                p.Name,
                p.IncludesAllExams,
                Prices = p.Prices.Select(pp => new
                {
                    pp.DurationMonths,
                    pp.PriceIdr,
                    pp.IsActive,
                    Subscriptions = db.Subscriptions.Count(s => s.PlanPriceId == pp.Id),
                }).ToList(),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. plans
            .OrderBy(p => p.Code)
            .Select(p => new PlanPricing(
                p.Id,
                p.Code,
                p.Name,
                p.IncludesAllExams,
                [.. PlanPrice.AllowedDurationMonths.Select(months =>
                    p.Prices.FirstOrDefault(pp => pp.DurationMonths == months) is { } price
                        ? new PlanPackage(months, price.PriceIdr, price.IsActive, price.Subscriptions)
                        : new PlanPackage(months, null, false, 0))]))];
    }
}

/// <summary>
/// Saves a plan's packages (PAY-02, story ADM-09, D-53). One row per plan and duration (the unique key), so there is at
/// most one price, and one active price, per plan and duration. A package with subscriptions can't be removed, only
/// switched off. Every change is audited.
/// </summary>
public sealed class SavePlanPrices(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    /// <summary>Arg: months.</summary>
    public const string PriceRange = "Admin.Plans.PriceRange";

    /// <summary>Arg: months.</summary>
    public const string ActiveNeedsPrice = "Admin.Plans.ActiveNeedsPrice";

    /// <summary>Arg: months.</summary>
    public const string PriceInUse = "Admin.Plans.PriceInUse";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result<int>> HandleAsync(SavePlanPricesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = await db.Plans.Include(p => p.Prices).SingleOrDefaultAsync(p => p.Id == request.PlanId && p.Code != PlanCode.Free, cancellationToken).ConfigureAwait(false);
        if (plan is null || request.Packages.Any(p => !PlanPrice.AllowedDurationMonths.Contains(p.DurationMonths)))
        {
            return Result.Fail<int>(Problem.NotFound);
        }

        var priceIds = plan.Prices.Select(p => p.Id).ToList();
        var used = (await db.Subscriptions.Where(s => priceIds.Contains(s.PlanPriceId)).Select(s => s.PlanPriceId).Distinct()
            .ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();
        var problems = new List<Problem>();
        foreach (var input in request.Packages)
        {
            var existing = plan.Prices.FirstOrDefault(p => p.DurationMonths == input.DurationMonths);
            if (input.PriceIdr is { } price && price <= 0)
            {
                problems.Add(new Problem(PriceRange, null, input.DurationMonths));
            }
            else if (input.PriceIdr is null && input.IsActive)
            {
                problems.Add(new Problem(ActiveNeedsPrice, null, input.DurationMonths));
            }
            else if (input.PriceIdr is null && existing is not null && used.Contains(existing.Id))
            {
                problems.Add(new Problem(PriceInUse, null, input.DurationMonths));
            }
        }

        if (problems.Count > 0)
        {
            return Result.Fail<int>(problems);
        }

        var before = Snapshot(plan);
        foreach (var input in request.Packages)
        {
            var existing = plan.Prices.FirstOrDefault(p => p.DurationMonths == input.DurationMonths);
            if (input.PriceIdr is not { } price)
            {
                if (existing is not null)
                {
                    db.PlanPrices.Remove(existing);
                }

                continue;
            }

            if (existing is null)
            {
                existing = new PlanPrice { PlanId = plan.Id, DurationMonths = input.DurationMonths };
                plan.Prices.Add(existing);
            }

            existing.PriceIdr = price;
            existing.IsActive = input.IsActive;
        }

        var after = request.Packages.Where(p => p.PriceIdr is not null)
            .Select(p => new { months = p.DurationMonths, priceIdr = p.PriceIdr, isActive = p.IsActive }).ToList();
        if (JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after.OrderBy(p => p.months)))
        {
            db.AuditLogs.Add(new AuditLog
            {
                ActorUserId = currentUser.UserId ?? throw new InvalidOperationException("Price changes need a signed-in admin."),
                Action = "Plan.Prices",
                EntityName = nameof(Plan),
                EntityId = plan.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Changes = JsonSerializer.Serialize(new { before, after }, Json),
                CreatedAt = clock.UtcNow,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(after.Count);
    }

    private static object[] Snapshot(Plan plan) =>
        [.. plan.Prices.OrderBy(p => p.DurationMonths).Select(p => new { months = p.DurationMonths, priceIdr = (long?)p.PriceIdr, isActive = p.IsActive })];
}
