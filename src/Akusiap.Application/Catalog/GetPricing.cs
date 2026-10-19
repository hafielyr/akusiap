using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Catalog;

public sealed record PricingExam(long Id, string Title, long PriceIdr, int AccessDays);

/// <param name="MonthlyIdr">The price per month, rounded to whole Rupiah.</param>
public sealed record PricingPackage(long PlanPriceId, int DurationMonths, long PriceIdr, long MonthlyIdr, bool IsCheapestMonthly);

/// <param name="Exam">The exam the visitor came from, when it is a published paid app exam.</param>
/// <param name="LowestExamPriceIdr">"Mulai dari": the cheapest published paid exam, shown when there is no exam context.</param>
public sealed record Pricing(PricingExam? Exam, long? LowestExamPriceIdr, string? PlanName, IReadOnlyList<PricingPackage> Packages);

/// <summary>
/// The pricing comparison (CAT-03, story G-05): one exam next to the Learner plan's packages that are on sale, each with
/// its monthly equivalent; the cheapest per month is highlighted (the longest package wins a tie). All prices come from
/// the database (D-57).
/// </summary>
public sealed class GetPricing(IAppDbContext db)
{
    public async Task<Pricing> HandleAsync(long? examId, CancellationToken cancellationToken)
    {
        var paidExams = db.Exams.Where(e => e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published && !e.IsFree && e.PriceIdr != null && e.AccessDays != null);
        PricingExam? exam = null;
        if (examId is { } id)
        {
            exam = await paidExams.Where(e => e.Id == id)
                .Select(e => new PricingExam(e.Id, e.Title, e.PriceIdr!.Value, e.AccessDays!.Value))
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        long? lowest = exam is null
            ? await paidExams.MinAsync(e => e.PriceIdr, cancellationToken).ConfigureAwait(false)
            : null;

        var plan = await db.Plans
            .Where(p => p.Code == PlanCode.Learner && p.IsActive)
            .Select(p => new
            {
                p.Name,
                Prices = p.Prices.Where(pp => pp.IsActive).Select(pp => new { pp.Id, pp.DurationMonths, pp.PriceIdr }).ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var packages = (plan?.Prices ?? [])
            .OrderBy(p => p.DurationMonths)
            .Select(p => new { p.Id, p.DurationMonths, p.PriceIdr, Monthly = MonthlyEquivalent(p.PriceIdr, p.DurationMonths) })
            .ToList();
        var cheapest = packages.OrderBy(p => p.Monthly).ThenByDescending(p => p.DurationMonths).FirstOrDefault();

        return new Pricing(
            exam,
            lowest,
            plan?.Name,
            [.. packages.Select(p => new PricingPackage(p.Id, p.DurationMonths, p.PriceIdr, p.Monthly, p.Id == cheapest?.Id))]);
    }

    /// <summary>Price per month, rounded half up to whole Rupiah.</summary>
    public static long MonthlyEquivalent(long priceIdr, int months)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);
        return (long)Math.Round((decimal)priceIdr / months, MidpointRounding.AwayFromZero);
    }
}
