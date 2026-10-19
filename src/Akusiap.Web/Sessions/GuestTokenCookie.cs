namespace Akusiap.Web.Sessions;

/// <summary>
/// The guest token (SES-01): a random id in an HttpOnly cookie that ties a guest's attempts (trial or shared exam) to
/// this browser, so they can resume and later carry them into a new account (SHR-04, G-09). Kept 30 days (D-45).
/// </summary>
public static class GuestTokenCookie
{
    public const string Name = "akusiap_guest";
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static Guid? Read(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Cookies.TryGetValue(Name, out var value) && Guid.TryParse(value, out var token) && token != Guid.Empty
            ? token
            : null;
    }

    /// <summary>The browser's token, creating and setting a new one when it has none.</summary>
    public static Guid GetOrCreate(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (Read(context.Request) is { } existing)
        {
            return existing;
        }

        var token = Guid.NewGuid();
        context.Response.Cookies.Append(Name, token.ToString("N"), new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            IsEssential = true,
            MaxAge = Lifetime,
            Path = "/",
        });
        return token;
    }
}
