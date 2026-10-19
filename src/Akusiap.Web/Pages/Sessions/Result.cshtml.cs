using Akusiap.Application.Sessions;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>
/// <c>/sesi/{id}/hasil</c> (SES-07): score, pass/fail, time and a per-topic table. Trial results offer to buy the exam or
/// subscribe (guests go through sign-in first). An attempt still in progress goes back to the player; anyone else's is 404.
/// </summary>
public sealed class ResultModel(GetResult getResult, SubmitAttempt submitAttempt) : PageModel
{
    public AttemptResult Result { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var guestToken = User.Identity?.IsAuthenticated == true ? null : GuestTokenCookie.Read(Request);
        await submitAttempt.CloseIfOverdueAsync(id, guestToken, cancellationToken);
        var result = await getResult.HandleAsync(id, guestToken, cancellationToken);
        if (result is null)
        {
            return await getResult.IsOpenForViewerAsync(id, guestToken, cancellationToken)
                ? Redirect(SessionRoutes.Player(id))
                : NotFound();
        }

        Result = result;
        return Page();
    }
}
