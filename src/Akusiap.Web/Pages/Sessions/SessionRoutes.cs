using System.Globalization;

namespace Akusiap.Web.Pages.Sessions;

/// <summary>Session URLs (D-31 Indonesian slugs). The player is SES-03.</summary>
public static class SessionRoutes
{
    public static string Player(long attemptId) => $"/sesi/{attemptId.ToString(CultureInfo.InvariantCulture)}";
}
