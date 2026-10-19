using Akusiap.Application.Common;
using Akusiap.Application.Plans;
using Akusiap.Domain;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Plans;

/// <summary>A package row as posted: the price is text so "Rp99.000" works.</summary>
public sealed class PackageForm
{
    public int DurationMonths { get; set; }

    public string? Price { get; set; }

    public bool IsActive { get; set; }
}

/// <summary><c>/admin/paket</c> (PAY-02): one form per paid plan with its 1, 3, 6 and 12-month packages.</summary>
public sealed class IndexModel(ListPlanPricing listPricing, SavePlanPrices savePrices, IStringLocalizer<SharedResource> t) : PageModel
{
    public const string Route = "/admin/paket";

    public IReadOnlyList<PlanPricing> Plans { get; private set; } = [];

    /// <summary>The plan whose form had problems, with the problems and what was typed.</summary>
    public long? FailedPlanId { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    public IReadOnlyList<PackageForm> Posted { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Plans = await listPricing.HandleAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(long planId, List<PackageForm> packages, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var problems = new List<string>();
        var inputs = new List<PackageInput>();
        foreach (var package in packages)
        {
            long? price = null;
            if (!string.IsNullOrWhiteSpace(package.Price))
            {
                if (Money.TryParseIdr(package.Price, out var parsed))
                {
                    price = parsed;
                }
                else
                {
                    problems.Add(t["Admin.Plans.PriceInvalid", package.DurationMonths]);
                    continue;
                }
            }

            inputs.Add(new PackageInput(package.DurationMonths, price, package.IsActive));
        }

        if (problems.Count == 0)
        {
            var result = await savePrices.HandleAsync(new SavePlanPricesRequest(planId, inputs), cancellationToken);
            if (result.IsNotFound)
            {
                return NotFound();
            }

            if (result.Succeeded)
            {
                TempData["Flash"] = "Admin.Plans.Saved";
                return Redirect(Route);
            }

            problems.AddRange(result.Problems.Select(p => ProblemModelState.Describe(p, t)));
        }

        Plans = await listPricing.HandleAsync(cancellationToken);
        FailedPlanId = planId;
        Errors = problems;
        Posted = packages;
        return Page();
    }

    /// <summary>The value to show in a price box: what was typed after a failed save, else the saved price.</summary>
    public string? PriceText(PlanPricing plan, PlanPackage package)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(package);
        if (FailedPlanId == plan.PlanId && Posted.FirstOrDefault(p => p.DurationMonths == package.DurationMonths) is { } typed)
        {
            return typed.Price;
        }

        return package.PriceIdr?.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public bool ActiveValue(PlanPricing plan, PlanPackage package)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(package);
        return FailedPlanId == plan.PlanId && Posted.FirstOrDefault(p => p.DurationMonths == package.DurationMonths) is { } typed
            ? typed.IsActive
            : package.IsActive;
    }
}
