using System.Globalization;

namespace Akusiap.Domain;

/// <summary>
/// Display and input time for users: Asia/Jakarta (WIB). Indonesia has no daylight saving, so a fixed UTC+7 offset is
/// exact and avoids depending on the server's time-zone database (D-44). Stored times stay UTC.
/// </summary>
public static class JakartaTime
{
    public static TimeSpan Offset { get; } = TimeSpan.FromHours(7);

    /// <summary>The Jakarta wall-clock time for a UTC instant.</summary>
    public static DateTime ToLocal(DateTime utc) => DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);

    /// <summary>Today's date in Jakarta.</summary>
    public static DateOnly Today(DateTime utcNow) => DateOnly.FromDateTime(ToLocal(utcNow));

    /// <summary>The last second of <paramref name="date"/> in Jakarta, as UTC: access "until 31 Dec" lasts the whole day.</summary>
    public static DateTime EndOfDayUtc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(23, 59, 59)) - Offset, DateTimeKind.Utc);

    /// <summary>Midnight at the start of <paramref name="date"/> in Jakarta, as UTC.</summary>
    public static DateTime StartOfDayUtc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue) - Offset, DateTimeKind.Utc);

    /// <summary>"31 Des 2026 23.59 WIB" in the current culture.</summary>
    public static string Format(DateTime utc) =>
        ToLocal(utc).ToString("d MMM yyyy HH:mm", CultureInfo.CurrentCulture) + " WIB";

    /// <summary>"31 Des 2026" in the current culture.</summary>
    public static string FormatDate(DateTime utc) => ToLocal(utc).ToString("d MMM yyyy", CultureInfo.CurrentCulture);

    /// <summary>Whole Jakarta days from today until <paramref name="endUtc"/>'s date; 0 on the last day, negative once past.</summary>
    public static int DaysLeft(DateTime endUtc, DateTime utcNow) => Today(endUtc).DayNumber - Today(utcNow).DayNumber;
}
