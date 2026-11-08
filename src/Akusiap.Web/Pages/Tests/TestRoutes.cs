using System.Globalization;
using Akusiap.Domain.Content;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Pages.Tests;

/// <summary>Upcoming-test URLs (CPNS-01, CPNS-02) and how their dates read.</summary>
public static class TestRoutes
{
    public const string List = "/jadwal-tes";

    public static string Detail(string slug) => $"{List}/{Uri.EscapeDataString(slug)}";

    /// <summary>
    /// A date as the status allows: an official date in full ("18 Januari 2027"), an estimate as month and year only
    /// ("Januari 2027"), so an estimate never looks exact.
    /// </summary>
    public static string Date(DateOnly date, ScheduleStatus status) =>
        date.ToString(status == ScheduleStatus.Official ? "d MMMM yyyy" : "MMMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>"H-70" for an official date, "± 70 hari lagi" for an estimate, "Hari ini" on the day.</summary>
    public static string Countdown(int daysLeft, ScheduleStatus status, IStringLocalizer t)
    {
        ArgumentNullException.ThrowIfNull(t);
        return daysLeft switch
        {
            0 => t["Tests.Today"].Value,
            < 0 => t["Tests.Passed"].Value,
            _ when status == ScheduleStatus.Official => t["Tests.DaysLeft", daysLeft].Value,
            _ => t["Tests.DaysLeftEstimated", daysLeft].Value,
        };
    }
}
