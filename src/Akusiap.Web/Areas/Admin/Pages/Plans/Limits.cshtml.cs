using Akusiap.Application.Plans;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Plans;

/// <summary><c>/admin/paket/batas</c> (OWN-01): own-question, own-exam and import-row caps per plan.</summary>
public sealed class LimitsModel(ListPlanLimits listLimits, SavePlanLimits saveLimits, IStringLocalizer<SharedResource> t) : PageModel
{
    public const string Route = "/admin/paket/batas";

    public IReadOnlyList<PlanLimitsRow> Plans { get; private set; } = [];

    public long? FailedPlanId { get; private set; }

    public SavePlanLimitsRequest? Posted { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Plans = await listLimits.HandleAsync(cancellationToken);

    public async Task<IActionResult> OnPostAsync(long planId, string? maxOwnQuestions, string? maxOwnExams, string? maxImportRows, CancellationToken cancellationToken)
    {
        var request = new SavePlanLimitsRequest(planId, maxOwnQuestions, maxOwnExams, maxImportRows);
        var result = await saveLimits.HandleAsync(request, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            TempData["Flash"] = "Admin.Limits.Saved";
            return Redirect(Route);
        }

        ModelState.AddProblems(result.Problems, t, $"p{planId}");
        FailedPlanId = planId;
        Posted = request;
        Plans = await listLimits.HandleAsync(cancellationToken);
        return Page();
    }

    /// <summary>The text to show in a box: what was typed after a failed save, else the saved value.</summary>
    public string? Value(PlanLimitsRow plan, Func<SavePlanLimitsRequest, string?> typed, int? saved)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(typed);
        return FailedPlanId == plan.PlanId && Posted is not null
            ? typed(Posted)
            : saved?.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
