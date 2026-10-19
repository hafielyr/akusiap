using Akusiap.Application.Sessions;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>
/// Practice help for the player (SES-05): <c>POST /sesi/{id}/bantuan?handler=Check</c> returns the feedback for an
/// answered item and locks it; <c>?handler=Hint</c> returns the hint. JSON; 403 when the rules don't allow it, 404 for
/// someone else's attempt, 409 once the session has ended. Antiforgery token in the <c>RequestVerificationToken</c> header.
/// </summary>
public sealed class HelpModel(PracticeHelp help) : PageModel
{
    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostCheckAsync(long id, int position, CancellationToken cancellationToken)
    {
        var (outcome, feedback) = await help.CheckAsync(id, GuestToken(), position, cancellationToken);
        return outcome == HelpOutcome.Done ? new JsonResult(feedback) : Fail(outcome);
    }

    public async Task<IActionResult> OnPostHintAsync(long id, int position, CancellationToken cancellationToken)
    {
        var (outcome, hint) = await help.HintAsync(id, GuestToken(), position, cancellationToken);
        return outcome == HelpOutcome.Done ? new JsonResult(new { hint }) : Fail(outcome);
    }

    private Guid? GuestToken() => User.Identity?.IsAuthenticated == true ? null : GuestTokenCookie.Read(Request);

    private static StatusCodeResult Fail(HelpOutcome outcome) => new(outcome switch
    {
        HelpOutcome.NotFound => StatusCodes.Status404NotFound,
        HelpOutcome.Ended => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status403Forbidden,
    });
}
