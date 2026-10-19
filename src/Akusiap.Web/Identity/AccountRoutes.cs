namespace Akusiap.Web.Identity;

/// <summary>Account URLs (Indonesian slugs, D-31).</summary>
public static class AccountRoutes
{
    public const string Register = "/akun/daftar";
    public const string SignIn = "/akun/masuk";
    public const string SignOut = "/akun/keluar";

    /// <summary>Where a new or returning user lands: the exam catalog.</summary>
    public const string AfterSignIn = "/ujian";

    /// <summary>
    /// <paramref name="route"/> carrying a local <paramref name="returnUrl"/> along, so switching between sign-in and
    /// register keeps the destination. Anything that isn't a local path is dropped.
    /// </summary>
    public static string WithReturnUrl(string route, string? returnUrl) =>
        IsLocal(returnUrl) ? $"{route}?ReturnUrl={Uri.EscapeDataString(returnUrl!)}" : route;

    private static bool IsLocal(string? url) =>
        !string.IsNullOrEmpty(url) && url[0] == '/' && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'));
}
