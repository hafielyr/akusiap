using Akusiap.Application.Sessions;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>
/// <c>POST /sesi/{id}/jawab</c> (SES-04): saves one answer or flag from the player. 204 when saved, 400 for options that
/// don't fit the question, 404 for someone else's attempt, 409 once the session has ended. The antiforgery token comes in
/// the <c>RequestVerificationToken</c> header.
/// </summary>
public sealed class AnswerModel(SaveAnswer saveAnswer) : PageModel
{
    public IActionResult OnGet() => NotFound();

    public async Task<IActionResult> OnPostAsync(long id, int position, long[]? selected, bool flagged, int seconds, CancellationToken cancellationToken)
    {
        var guestToken = User.Identity?.IsAuthenticated == true ? null : GuestTokenCookie.Read(Request);
        var outcome = await saveAnswer.HandleAsync(new SaveAnswerRequest(id, guestToken, position, selected ?? [], flagged, seconds), cancellationToken);
        return outcome switch
        {
            SaveAnswerOutcome.Saved => new NoContentResult(),
            SaveAnswerOutcome.NotFound => NotFound(),
            SaveAnswerOutcome.Ended => StatusCode(StatusCodes.Status409Conflict),
            _ => BadRequest(),
        };
    }
}
