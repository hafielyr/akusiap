using System.Security.Claims;
using Akusiap.Application.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Akusiap.Web.Identity;

/// <summary>
/// Hooks the device limit (PAY-13, D-141) into the sign-in cookie. A long-lived <see cref="CookieName"/> cookie names
/// the browser. Signing in registers it (pushing out devices over a subscriber's limit) and puts the device id in the
/// principal; every request checks the device is still allowed, and a pushed-out device is signed out with a notice.
/// Signing out frees the device.
/// </summary>
public static class DeviceTracking
{
    public const string CookieName = "akusiap_device";
    public const string NoticeCookieName = "akusiap_pushed_out";
    public const string ClaimType = "akusiap:device";

    public static void Attach(CookieAuthenticationOptions options, bool secureCookies)
    {
        ArgumentNullException.ThrowIfNull(options);
        var validateStamp = options.Events.OnValidatePrincipal;
        var signingIn = options.Events.OnSigningIn;
        var signingOut = options.Events.OnSigningOut;

        options.Events.OnSigningIn = async context =>
        {
            await signingIn(context);
            if (context.Principal?.Identity is ClaimsIdentity identity && UserId(context.Principal) is { } userId)
            {
                var key = DeviceKey(context.HttpContext, secureCookies);
                var label = DeviceLabels.From(context.HttpContext.Request.Headers.UserAgent.ToString());
                var devices = context.HttpContext.RequestServices.GetRequiredService<DeviceSessions>();
                var deviceId = await devices.SignedInAsync(userId, key, label, context.HttpContext.RequestAborted);
                foreach (var old in identity.FindAll(ClaimType).ToList())
                {
                    identity.RemoveClaim(old);
                }

                identity.AddClaim(new Claim(ClaimType, deviceId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
        };

        options.Events.OnValidatePrincipal = async context =>
        {
            await validateStamp(context);
            if (context.Principal is not { } principal || UserId(principal) is not { } userId || DeviceId(principal) is not { } deviceId)
            {
                return;
            }

            var devices = context.HttpContext.RequestServices.GetRequiredService<DeviceSessions>();
            if (!await devices.IsActiveAsync(userId, deviceId, context.HttpContext.RequestAborted))
            {
                context.RejectPrincipal();
                context.HttpContext.Response.Cookies.Append(NoticeCookieName, "1", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Lax, Secure = secureCookies, IsEssential = true });
                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            }
        };

        options.Events.OnSigningOut = async context =>
        {
            await signingOut(context);
            var principal = context.HttpContext.User;
            if (UserId(principal) is { } userId && DeviceId(principal) is { } deviceId)
            {
                var devices = context.HttpContext.RequestServices.GetRequiredService<DeviceSessions>();
                await devices.SignedOutAsync(userId, deviceId, context.HttpContext.RequestAborted);
            }
        };
    }

    /// <summary>Keeps the device id when the security stamp check rebuilds the principal.</summary>
    public static Task KeepDeviceClaim(SecurityStampRefreshingPrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.CurrentPrincipal?.FindFirst(ClaimType) is { } claim && context.NewPrincipal?.Identity is ClaimsIdentity identity && !identity.HasClaim(c => c.Type == ClaimType))
        {
            identity.AddClaim(new Claim(ClaimType, claim.Value));
        }

        return Task.CompletedTask;
    }

    public static long? DeviceId(ClaimsPrincipal? principal) =>
        long.TryParse(principal?.FindFirst(ClaimType)?.Value, System.Globalization.CultureInfo.InvariantCulture, out var id) ? id : null;

    private static Guid? UserId(ClaimsPrincipal? principal) =>
        Guid.TryParse(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    private static Guid DeviceKey(HttpContext http, bool secureCookies)
    {
        if (Guid.TryParse(http.Request.Cookies[CookieName], out var key))
        {
            return key;
        }

        key = Guid.NewGuid();
        http.Response.Cookies.Append(CookieName, key.ToString(), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = secureCookies,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(730),
        });
        return key;
    }
}

/// <summary>A short "Chrome · Android" name for a device from its User-Agent (PAY-13).</summary>
public static class DeviceLabels
{
    public static string From(string? userAgent)
    {
        var ua = userAgent ?? string.Empty;
        var browser = Has(ua, "Edg/") ? "Edge"
            : Has(ua, "OPR/") || Has(ua, "Opera") ? "Opera"
            : Has(ua, "SamsungBrowser") ? "Samsung Internet"
            : Has(ua, "Firefox/") ? "Firefox"
            : Has(ua, "Chrome/") || Has(ua, "CriOS") ? "Chrome"
            : Has(ua, "Safari/") ? "Safari"
            : null;
        var system = Has(ua, "Android") ? "Android"
            : Has(ua, "iPhone") ? "iPhone"
            : Has(ua, "iPad") ? "iPad"
            : Has(ua, "Windows") ? "Windows"
            : Has(ua, "Mac OS X") || Has(ua, "Macintosh") ? "Mac"
            : Has(ua, "Linux") ? "Linux"
            : null;
        return (browser, system) switch
        {
            (null, null) => "-",
            (null, _) => system!,
            (_, null) => browser!,
            _ => $"{browser} · {system}",
        };
    }

    private static bool Has(string ua, string token) => ua.Contains(token, StringComparison.OrdinalIgnoreCase);
}
