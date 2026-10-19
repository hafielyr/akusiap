using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace Akusiap.Web.Localization;

public static class LocalizationSetup
{
    public const string DefaultCulture = "id-ID";

    /// <summary>UI languages offered in the switcher. Indonesian is the default (ID-01).</summary>
    public static readonly IReadOnlyList<(string Code, string Label)> Languages =
    [
        ("id-ID", "Bahasa Indonesia"),
        ("en", "English"),
    ];

    public static IServiceCollection AddAppLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.Configure<RequestLocalizationOptions>(options =>
        {
            var cultures = Languages.Select(l => new CultureInfo(l.Code)).ToList();
            options.DefaultRequestCulture = new RequestCulture(DefaultCulture);
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            // Query string (?culture=en) and the cookie only: the browser's Accept-Language is ignored
            // so that Indonesian stays the default even on phones set to English.
            options.RequestCultureProviders =
            [
                new QueryStringRequestCultureProvider(),
                new CookieRequestCultureProvider(),
            ];
        });
        return services;
    }

    public static bool IsSupported(string culture) =>
        Languages.Any(l => string.Equals(l.Code, culture, StringComparison.OrdinalIgnoreCase));

    /// <summary>The UI culture for a user's preferred language (<c>id</c> or <c>en</c>).</summary>
    public static string CultureFor(string preferredLanguage) =>
        preferredLanguage == Domain.Users.Languages.English ? "en" : DefaultCulture;

    /// <summary>Remembers the UI language in the culture cookie for a year (the switcher, sign-in and the profile page).</summary>
    public static void SetCultureCookie(HttpResponse response, string culture)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1), IsEssential = true, HttpOnly = true, SameSite = SameSiteMode.Lax });
    }
}
