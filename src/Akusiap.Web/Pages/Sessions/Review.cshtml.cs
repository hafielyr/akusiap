using Akusiap.Application.Sessions;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages.Sessions;

/// <summary><c>/sesi/{id}/pembahasan</c> (SES-08): the answer review, filtered with <c>?tampil=salah|ditandai</c>.</summary>
public sealed class ReviewModel(GetReview getReview) : PageModel
{
    [BindProperty(SupportsGet = true, Name = "tampil")]
    public string? Show { get; set; }

    public ReviewFilter Filter => Show switch
    {
        "salah" => ReviewFilter.Wrong,
        "ditandai" => ReviewFilter.Flagged,
        _ => ReviewFilter.All,
    };

    public AttemptReview Review { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var guestToken = User.Identity?.IsAuthenticated == true ? null : GuestTokenCookie.Read(Request);
        var review = await getReview.HandleAsync(id, guestToken, Filter, cancellationToken);
        if (review is null)
        {
            return NotFound();
        }

        Review = review;
        return Page();
    }
}
