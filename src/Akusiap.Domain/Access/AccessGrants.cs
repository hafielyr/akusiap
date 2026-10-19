namespace Akusiap.Domain.Access;

/// <summary>
/// End dates for access an admin grants or changes by hand (ENT-03, story ADM-18). Granting again extends access that is
/// still running, like buying again does (BR-08, BR-10); access that already ended starts again from now.
/// </summary>
public static class AccessGrants
{
    public const int MaxPlanDays = 3660;

    /// <summary>The new end of an exam grant: a custom Jakarta date (whole day), or the exam's access days.</summary>
    /// <returns>Null when there is neither a custom date nor access days.</returns>
    public static DateTime? ExamGrantEnd(DateTime now, DateTime? runningEnd, int? accessDays, DateOnly? customDate)
    {
        if (customDate is { } date)
        {
            return JakartaTime.EndOfDayUtc(date);
        }

        return accessDays is > 0 ? From(now, runningEnd).AddDays(accessDays.Value) : null;
    }

    /// <summary>The new end of a plan grant of <paramref name="days"/> days.</summary>
    public static DateTime PlanGrantEnd(DateTime now, DateTime? runningEnd, int days)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);
        return From(now, runningEnd).AddDays(days);
    }

    public static bool IsValidPlanDays(int days) => days is >= 1 and <= MaxPlanDays;

    /// <summary>A new end date must still be in the future.</summary>
    public static bool IsInFuture(DateTime endUtc, DateTime now) => endUtc > now;

    /// <summary>Ending now never moves an end date that already passed.</summary>
    public static DateTime EndNow(DateTime now, DateTime? currentEnd) =>
        currentEnd is { } end && end < now ? end : now;

    private static DateTime From(DateTime now, DateTime? runningEnd) =>
        runningEnd is { } end && end > now ? end : now;
}
