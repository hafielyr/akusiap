using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary><c>/learn</c>: links to the learner's pages and, until the email is confirmed, a resend button (AC-02).</summary>
public sealed class IndexModel(UserManager<User> users, AccountEmails emails) : PageModel
{
    public bool EmailConfirmed { get; private set; } = true;

    /// <summary>The resource key of the message after a resend, if any.</summary>
    [TempData]
    public string? ResendMessage { get; set; }

    public async Task OnGetAsync()
    {
        var user = await users.GetUserAsync(User);
        EmailConfirmed = user?.EmailConfirmed ?? true;
    }

    public async Task<IActionResult> OnPostResendConfirmationAsync(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is { EmailConfirmed: false })
        {
            var sent = await emails.SendConfirmationAsync(user, cancellationToken);
            ResendMessage = !sent ? "Account.Confirm.SendFailed" : emails.Delivery switch
            {
                Application.Abstractions.EmailDelivery.File => "Account.Confirm.SavedToFile",
                Application.Abstractions.EmailDelivery.Dropped => "Account.Confirm.NotSent",
                _ => "Account.Confirm.Resent",
            };
        }

        return Redirect("/learn");
    }
}
