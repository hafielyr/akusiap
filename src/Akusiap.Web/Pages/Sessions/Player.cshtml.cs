using System.Text.Json;
using Akusiap.Application.Sessions;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>
/// <c>/sesi/{id}</c> (SES-03): one question per screen, run by the Alpine component in <c>quiz-player.js</c>. The owner
/// (or the guest token) is checked on every request; anyone else gets 404.
/// </summary>
public sealed class PlayerModel(GetPlayer getPlayer, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) : PageModel
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public PlayerView View { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var guestToken = User.Identity?.IsAuthenticated == true ? null : GuestTokenCookie.Read(Request);
        var view = await getPlayer.HandleAsync(id, guestToken, cancellationToken);
        if (view is null)
        {
            return NotFound();
        }

        View = view;
        return Page();
    }

    /// <summary>The state handed to the Alpine component (no correct answers).</summary>
    public string StateJson() => JsonSerializer.Serialize(
        new
        {
            attemptId = View.AttemptId,
            saveUrl = $"{SessionRoutes.Player(View.AttemptId)}/jawab",
            token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken,
            secondsLeft = View.SecondsLeft,
            items = View.Items.Select(i => new
            {
                i.Position,
                type = i.Type.ToString(),
                i.Text,
                options = i.Options,
                selected = i.Selected,
                flagged = i.IsFlagged,
            }),
        },
        Json);
}
