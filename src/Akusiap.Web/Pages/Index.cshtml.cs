using Akusiap.Domain.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages;

/// <summary>The home page. Signed-in learners go to today's reviews instead (SRS-03); guests and admins see it.</summary>
public sealed class IndexModel : PageModel
{
    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true && User.IsInRole(RoleNames.Learner) && !User.IsInRole(RoleNames.Admin)
            ? Redirect(Areas.Learn.Pages.TodayModel.Route)
            : Page();
}
