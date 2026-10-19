using System.ComponentModel.DataAnnotations;
using Akusiap.Domain.Users;
using Akusiap.Web.Identity;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Account.Pages;

/// <summary>Email sign-in. Deactivated users are told so only after the right password, so the page never reveals account state.</summary>
public sealed class LoginModel(UserManager<User> users, SignInManager<User> signIn, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public LoginInput Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>Set after a password reset (AC-02) to show "password changed".</summary>
    [BindProperty(SupportsGet = true, Name = "reset")]
    public bool PasswordWasReset { get; set; }

    public IActionResult OnGet() =>
        User.Identity?.IsAuthenticated == true ? LocalRedirect(SafeReturnUrl) : Page();

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await users.FindByEmailAsync(Input.Email.Trim());
        if (user is null)
        {
            return Fail("Account.SignIn.Failed");
        }

        var result = await signIn.PasswordSignInAsync(user, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Succeeded)
        {
            SetCultureCookie(user.PreferredLanguage);
            return LocalRedirect(SafeReturnUrl);
        }

        if (result.IsLockedOut)
        {
            return Fail("Account.SignIn.LockedOut");
        }

        // NotAllowed is checked before the password, so confirm the password before saying the account is deactivated.
        if (result.IsNotAllowed && await users.CheckPasswordAsync(user, Input.Password))
        {
            return Fail("Account.SignIn.Deactivated");
        }

        return Fail("Account.SignIn.Failed");
    }

    private string SafeReturnUrl => Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : AccountRoutes.AfterSignIn;

    private PageResult Fail(string messageKey)
    {
        ModelState.AddModelError(string.Empty, t[messageKey]);
        return Page();
    }

    private void SetCultureCookie(string preferredLanguage)
    {
        var culture = preferredLanguage == Languages.English ? "en" : LocalizationSetup.DefaultCulture;
        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
    }
}

public sealed class LoginInput
{
    [Display(Name = "Account.Email")]
    [Required(ErrorMessage = "Validation.Required")]
    [EmailAddress(ErrorMessage = "Validation.Email")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Account.Password")]
    [Required(ErrorMessage = "Validation.Required")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Account.SignIn.RememberMe")]
    public bool RememberMe { get; set; } = true;
}
