using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Pages;

/// <summary>Switches the UI language by setting the culture cookie, then returns to the previous page.</summary>
/// <remarks>A plain GET link so the switcher works without JavaScript; it only changes a display preference.</remarks>
public sealed class LanguageModel : PageModel
{
    public IActionResult OnGet(string culture, string? returnUrl)
    {
        if (LocalizationSetup.IsSupported(culture))
        {
            Response.Cookies.Append(
                CookieRequestCultureProvider.DefaultCookieName,
                CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
                new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
