using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Account.Pages;

/// <summary>
/// <c>/akun/google</c> (AC-03): POST starts the Google challenge; Google comes back to <c>?handler=Callback</c>, which
/// signs the user in (linking or creating the account) and returns to the local ReturnUrl. Failures are shown here.
/// </summary>
public sealed class GoogleModel(
    SignInManager<User> signIn,
    GoogleAccounts accounts,
    IAuthenticationSchemeProvider schemes) : PageModel
{
    /// <summary>The resource key of the message to show, when sign-in failed.</summary>
    public string? Error { get; private set; }

    public IActionResult OnGet() => Redirect(AccountRoutes.SignIn);

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (!await GoogleSignIn.IsEnabledAsync(schemes))
        {
            return NotFound();
        }

        var callback = Url.Page("/Google", pageHandler: "Callback", values: new { area = "Account", returnUrl = Safe(returnUrl) });
        var properties = signIn.ConfigureExternalAuthenticationProperties(GoogleSignIn.Scheme, callback);
        return Challenge(properties, GoogleSignIn.Scheme);
    }

    public async Task<IActionResult> OnGetCallbackAsync(string? returnUrl, string? remoteError, CancellationToken cancellationToken)
    {
        var login = remoteError is null ? await signIn.GetExternalLoginInfoAsync() : null;
        if (login is null)
        {
            Error = "Account.Google.Failed";
            return Page();
        }

        var (outcome, user) = await accounts.FindOrCreateAsync(login);
        cancellationToken.ThrowIfCancellationRequested();
        if (outcome != GoogleAccountOutcome.SignedIn || user is null)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            Error = outcome == GoogleAccountOutcome.Deactivated ? "Account.SignIn.Deactivated" : "Account.Google.NoEmail";
            return Page();
        }

        await signIn.SignInAsync(user, isPersistent: true, authenticationMethod: login.LoginProvider);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        LocalizationSetup.SetCultureCookie(Response, LocalizationSetup.CultureFor(user.PreferredLanguage));
        return LocalRedirect(Safe(returnUrl));
    }

    private string Safe(string? returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl! : AccountRoutes.AfterSignIn;
}
