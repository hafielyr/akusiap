using System.Globalization;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>Session URLs (D-31 Indonesian slugs). The player is SES-03.</summary>
public static class SessionRoutes
{
    public static string Player(long attemptId) => $"/sesi/{attemptId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The result page of a finished attempt (SES-07).</summary>
    public static string Result(long attemptId) => $"{Player(attemptId)}/hasil";

    /// <summary>The answer review of a finished attempt (SES-08); <paramref name="show"/> is <c>salah</c> or <c>ditandai</c>.</summary>
    public static string Review(long attemptId, string? show = null) => show is null ? $"{Player(attemptId)}/pembahasan" : $"{Player(attemptId)}/pembahasan?tampil={show}";

    /// <summary>"Ulangi yang salah" (SES-09), POST only.</summary>
    public static string Retake(long attemptId) => $"{Player(attemptId)}/ulangi";
}
