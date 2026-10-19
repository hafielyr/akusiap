using Akusiap.Domain;
using Akusiap.Domain.Access;

namespace Akusiap.UnitTests.Domain.Access;

/// <summary>ENT-03: end dates for access granted or changed by an admin, and Jakarta dates.</summary>
public sealed class AccessGrantsTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 3, 0, 0, DateTimeKind.Utc); // 10:00 WIB

    [Fact]
    public void Exam_grant_without_running_access_starts_now()
    {
        Assert.Equal(Now.AddDays(30), AccessGrants.ExamGrantEnd(Now, runningEnd: null, accessDays: 30, customDate: null));
        Assert.Equal(Now.AddDays(30), AccessGrants.ExamGrantEnd(Now, Now.AddDays(-5), 30, null));
    }

    [Fact]
    public void Exam_grant_extends_running_access()
    {
        Assert.Equal(Now.AddDays(40), AccessGrants.ExamGrantEnd(Now, Now.AddDays(10), 30, null));
    }

    [Fact]
    public void Custom_date_lasts_the_whole_jakarta_day()
    {
        var end = AccessGrants.ExamGrantEnd(Now, Now.AddDays(10), 30, new DateOnly(2026, 12, 31));

        Assert.Equal(new DateTime(2026, 12, 31, 16, 59, 59, DateTimeKind.Utc), end);
        Assert.Equal(DateTimeKind.Utc, end!.Value.Kind);
    }

    [Fact]
    public void Exam_grant_needs_access_days_or_a_date()
    {
        Assert.Null(AccessGrants.ExamGrantEnd(Now, null, accessDays: null, customDate: null));
        Assert.Null(AccessGrants.ExamGrantEnd(Now, null, accessDays: 0, customDate: null));
    }

    [Fact]
    public void Plan_grant_extends_running_access_or_starts_now()
    {
        Assert.Equal(Now.AddDays(7), AccessGrants.PlanGrantEnd(Now, null, 7));
        Assert.Equal(Now.AddDays(37), AccessGrants.PlanGrantEnd(Now, Now.AddDays(30), 7));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(365, true)]
    [InlineData(AccessGrants.MaxPlanDays, true)]
    [InlineData(AccessGrants.MaxPlanDays + 1, false)]
    public void Plan_days_are_bounded(int days, bool valid)
    {
        Assert.Equal(valid, AccessGrants.IsValidPlanDays(days));
    }

    [Fact]
    public void Ending_now_keeps_an_end_that_already_passed()
    {
        Assert.Equal(Now, AccessGrants.EndNow(Now, Now.AddDays(3)));
        Assert.Equal(Now.AddDays(-3), AccessGrants.EndNow(Now, Now.AddDays(-3)));
        Assert.Equal(Now, AccessGrants.EndNow(Now, null));
    }

    [Fact]
    public void Jakarta_today_and_days_left_follow_wib_midnight()
    {
        var lateEvening = new DateTime(2026, 10, 19, 17, 30, 0, DateTimeKind.Utc); // 00:30 WIB on the 20th

        Assert.Equal(new DateOnly(2026, 10, 20), JakartaTime.Today(lateEvening));
        Assert.Equal(new DateOnly(2026, 10, 19), JakartaTime.Today(Now));
        Assert.Equal(0, JakartaTime.DaysLeft(JakartaTime.EndOfDayUtc(new DateOnly(2026, 10, 19)), Now));
        Assert.Equal(12, JakartaTime.DaysLeft(JakartaTime.EndOfDayUtc(new DateOnly(2026, 10, 31)), Now));
        Assert.Equal(-1, JakartaTime.DaysLeft(Now.AddDays(-1), Now));
    }
}
