using Akusiap.Application.Moderation;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>
/// <c>/sesi/{id}/lapor/{position}</c> (SES-11): the "Laporkan soal" form for a question in the viewer's own attempt,
/// signed in or guest. After sending it shows a thank-you with a link back to the review (or the player while open).
/// </summary>
public sealed class ReportModel(QuestionReports reports, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public ReportReason? Reason { get; set; }

    [BindProperty]
    public string? Details { get; set; }

    [BindProperty(SupportsGet = true, Name = "terkirim")]
    public bool Sent { get; set; }

    public ReportTarget Target { get; private set; } = null!;

    public string BackUrl => Target.SessionEnded ? $"{SessionRoutes.Review(Target.AttemptId)}" : SessionRoutes.Player(Target.AttemptId);

    public async Task<IActionResult> OnGetAsync(long id, int position, CancellationToken cancellationToken)
    {
        var target = await reports.GetTargetAsync(id, position, GuestToken(), cancellationToken);
        if (target is null)
        {
            return NotFound();
        }

        Target = target;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id, int position, CancellationToken cancellationToken)
    {
        var result = await reports.ReportAsync(new ReportQuestionRequest(id, position, GuestToken(), Reason, Details), cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            foreach (var problem in result.Problems)
            {
                ModelState.AddModelError(problem.Field ?? string.Empty, ProblemModelState.Describe(problem, t));
            }

            Target = (await reports.GetTargetAsync(id, position, GuestToken(), cancellationToken))!;
            return Page();
        }

        return Redirect($"{SessionRoutes.Player(id)}/lapor/{position}?terkirim=true");
    }

    private Guid? GuestToken() => User.Identity?.IsAuthenticated == true ? null : GuestTokenCookie.Read(Request);
}
