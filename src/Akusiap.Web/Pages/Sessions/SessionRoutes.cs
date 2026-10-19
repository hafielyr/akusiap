using System.Globalization;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>Session URLs (D-31 Indonesian slugs). The player is SES-03.</summary>
public static class SessionRoutes
{
    public static string Player(long attemptId) => $"/sesi/{attemptId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Where a finished attempt goes: the player shows the score until SES-07 adds the result page.</summary>
    public static string Result(long attemptId) => Player(attemptId);
}
