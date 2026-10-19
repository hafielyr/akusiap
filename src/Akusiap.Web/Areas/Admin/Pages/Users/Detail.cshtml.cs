using Akusiap.Application.Users;
using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Users;

/// <summary><c>/admin/pengguna/{id}</c> (USR-01): profile, roles, access (managed on /admin/akses) and attempt count.</summary>
public sealed class DetailModel(GetUserDetail getUser, UserManager<User> users, AccountEmails emails) : PageModel
{
    public UserDetail Detail { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await getUser.HandleAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        return Page();
    }

    /// <summary>Sends the "set your password" email again (USR-02), for example when the first link expired.</summary>
    public async Task<IActionResult> OnPostInviteAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        TempData["Flash"] = await emails.SendInviteAsync(user, cancellationToken) ? "Admin.Users.InviteSent" : "Account.Confirm.SendFailed";
        return Redirect($"{IndexModel.Route}/{id}");
    }
}
