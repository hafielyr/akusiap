using Akusiap.Domain.Planning;

namespace Akusiap.UnitTests.Domain.Planning;

/// <summary>PLN-01 (D-120): exam goal dates and the countdown in Jakarta days.</summary>
public sealed class StudyGoalTests
{
    private static readonly DateOnly Today = new(2026, 11, 7);

    [Theory]
    [InlineData(23, 23)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(-2, -2)]
    public void Days_left_counts_calendar_days_to_the_exam(int offset, int expected)
    {
        var goal = new StudyGoal { ExamDate = Today.AddDays(offset) };

        Assert.Equal(expected, goal.DaysLeft(Today));
    }

    [Fact]
    public void Tomorrow_is_the_earliest_date_allowed()
    {
        Assert.Equal(StudyGoalDateCheck.Ok, StudyGoal.CheckDate(Today.AddDays(1), Today));
        Assert.Equal(StudyGoalDateCheck.NotInFuture, StudyGoal.CheckDate(Today, Today));
        Assert.Equal(StudyGoalDateCheck.NotInFuture, StudyGoal.CheckDate(Today.AddDays(-1), Today));
    }

    [Fact]
    public void Two_years_ahead_is_the_latest_date_allowed()
    {
        Assert.Equal(StudyGoalDateCheck.Ok, StudyGoal.CheckDate(Today.AddYears(2), Today));
        Assert.Equal(StudyGoalDateCheck.TooFar, StudyGoal.CheckDate(Today.AddYears(2).AddDays(1), Today));
    }
}
