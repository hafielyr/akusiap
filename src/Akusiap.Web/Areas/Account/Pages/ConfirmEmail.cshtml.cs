using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Account.Pages;

/// <summary>The link from the confirmation email (AC-02). Works signed in or not.</summary>
public sealed class ConfirmEmailModel(UserManager<User> users) : PageModel
{
    public bool Confirmed { get; private set; }

    public async Task OnGetAsync(Guid? userId, string? code, CancellationToken cancellationToken)
    {
        var token = AccountEmails.Decode(code);
        var user = userId is { } id ? await users.FindByIdAsync(id.ToString()) : null;
        cancellationToken.ThrowIfCancellationRequested();
        Confirmed = user is not null && token is not null && (await users.ConfirmEmailAsync(user, token)).Succeeded;
    }
}
