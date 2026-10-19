using Akusiap.Application.Sessions;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>
/// <c>POST /sesi/{id}/ulangi</c> (SES-09): "Ulangi yang salah" from the result or review page starts a WrongOnly attempt
/// and opens it. Problems go back to the result page as a message; someone else's attempt is 404.
/// </summary>
public sealed class RetakeModel(StartAttempt startAttempt) : PageModel
{
    public IActionResult OnGet(long id) => Redirect(SessionRoutes.Result(id));

    public async Task<IActionResult> OnPostAsync(long id, CancellationToken cancellationToken)
    {
        var guestToken = User.Identity?.IsAuthenticated == true ? null : GuestTokenCookie.Read(Request);
        var result = await startAttempt.RetakeWrongAsync(id, guestToken, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            TempData["Flash"] = result.Problems[0].Code;
            return Redirect(SessionRoutes.Result(id));
        }

        return Redirect(SessionRoutes.Player(result.Value!.AttemptId));
    }
}
