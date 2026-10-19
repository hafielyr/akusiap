namespace Akusiap.Domain.Access;

public enum RenewalReminder
{
    None,
    SevenDays,
    OneDay,
}

/// <summary>
/// Which package-ending reminder is due (PAY-08, story P-07, D-77), counted in Jakarta calendar days so the daily
/// 09:00 WIB run sends it on the right day: the 7-day one from 7 days before the end (later if a run was missed), the
/// 1-day one on the day before or the last day. Each is sent once, guarded by its sent-at time. Nothing once ended.
/// </summary>
public static class RenewalReminderPolicy
{
    public static RenewalReminder Due(DateTime periodEndUtc, DateTime utcNow, DateTime? sevenDaySentAt, DateTime? oneDaySentAt)
    {
        if (periodEndUtc <= utcNow)
        {
            return RenewalReminder.None;
        }

        var daysLeft = JakartaTime.DaysLeft(periodEndUtc, utcNow);
        if (daysLeft <= 1)
        {
            return oneDaySentAt is null ? RenewalReminder.OneDay : RenewalReminder.None;
        }

        return daysLeft <= 7 && sevenDaySentAt is null ? RenewalReminder.SevenDays : RenewalReminder.None;
    }
}
