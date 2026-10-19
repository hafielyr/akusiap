using Akusiap.Web.Localization;
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
            LocalizationSetup.SetCultureCookie(Response, culture);
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }
}
