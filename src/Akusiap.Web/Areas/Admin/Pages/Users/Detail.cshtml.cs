using Akusiap.Application.Users;
using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Users;

/// <summary><c>/admin/pengguna/{id}</c> (USR-01): profile, roles, access (managed on /admin/akses) and attempt count.</summary>
public sealed class DetailModel(
    GetUserDetail getUser,
    UserManager<User> users,
    AccountEmails emails,
    Akusiap.Application.Abstractions.IAppDbContext db,
    Akusiap.Application.Abstractions.ICurrentUser currentUser,
    Akusiap.Application.Abstractions.IClock clock) : PageModel
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

    /// <summary>
    /// Deactivates or reactivates the account (USR-03, story ADM-16). Deactivating also changes the security stamp, so
    /// every signed-in session ends on its next request; nothing else about the account changes. Admins can't
    /// deactivate themselves.
    /// </summary>
    public async Task<IActionResult> OnPostActiveAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }

        if (!active && id == currentUser.UserId)
        {
            TempData["Flash"] = "Admin.Users.CannotDeactivateSelf";
            return Redirect($"{IndexModel.Route}/{id}");
        }

        if (user.IsActive != active)
        {
            user.IsActive = active;
            user.DeactivatedAt = active ? null : clock.UtcNow;
            var updated = await users.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                throw new InvalidOperationException($"Could not update the user: {string.Join("; ", updated.Errors.Select(e => e.Code))}");
            }

            if (!active)
            {
                await users.UpdateSecurityStampAsync(user);
            }

            db.AuditLogs.Add(new Akusiap.Domain.Auditing.AuditLog
            {
                ActorUserId = currentUser.UserId!.Value,
                Action = active ? "User.Reactivate" : "User.Deactivate",
                EntityName = nameof(User),
                EntityId = user.Id.ToString(),
                CreatedAt = clock.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        TempData["Flash"] = active ? "Admin.Users.Reactivated" : "Admin.Users.Deactivated";
        return Redirect($"{IndexModel.Route}/{id}");
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
