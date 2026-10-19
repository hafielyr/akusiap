namespace Akusiap.Web.Identity;

/// <summary>Account URLs (Indonesian slugs, D-31).</summary>
public static class AccountRoutes
{
    public const string Register = "/akun/daftar";
    public const string SignIn = "/akun/masuk";
    public const string SignOut = "/akun/keluar";

    /// <summary>Where a new or returning user lands: the exam catalog.</summary>
    public const string AfterSignIn = "/ujian";
}
