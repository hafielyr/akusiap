using Akusiap.Domain.Planning;

namespace Akusiap.UnitTests.Domain.Planning;

/// <summary>PLN-02 (D-121): the review phase, the daily new-question quota and how missed days spread out.</summary>
public sealed class StudyPlannerTests
{
    private static readonly DateOnly Start = new(2026, 11, 7);

    [Theory]
    [InlineData(120, 7)]
    [InlineData(35, 7)]
    [InlineData(34, 6)]
    [InlineData(10, 2)]
    [InlineData(5, 1)]
    [InlineData(2, 1)]
    [InlineData(1, 0)]
    public void The_review_phase_is_7_days_or_20_percent_of_a_short_plan(int planDays, int expected)
    {
        Assert.Equal(expected, StudyPlanner.ReviewPhaseDays(planDays));
    }

    [Fact]
    public void The_quota_spreads_unanswered_questions_over_the_learning_days()
    {
        // 47 days to the exam: 7 review days, 40 learning days.
        var day = StudyPlanner.Plan(Start, Start.AddDays(47), Start, unansweredAtStartOfDay: 100, newDoneToday: 0);

        Assert.Equal(StudyPhase.Learning, day.Phase);
        Assert.Equal(47, day.DaysLeft);
        Assert.Equal(40, day.LearningDaysLeft);
        Assert.Equal(3, day.NewQuota); // ceil(100 / 40)
        Assert.Equal(3, day.NewLeftToday);
        Assert.False(day.OverCap);
    }

    [Fact]
    public void Answering_today_lowers_what_is_left_today_but_not_the_quota()
    {
        // 17 days: 3 review days, 14 learning days, ceil(100 / 14) = 8.
        var start = StudyPlanner.Plan(Start, Start.AddDays(17), Start, unansweredAtStartOfDay: 100, newDoneToday: 0);
        var later = StudyPlanner.Plan(Start, Start.AddDays(17), Start, unansweredAtStartOfDay: 100, newDoneToday: 5);
        var beyond = StudyPlanner.Plan(Start, Start.AddDays(17), Start, unansweredAtStartOfDay: 100, newDoneToday: 12);

        Assert.Equal((8, 8), (start.NewQuota, start.NewLeftToday));
        Assert.Equal((8, 3), (later.NewQuota, later.NewLeftToday));
        Assert.Equal((8, 0), (beyond.NewQuota, beyond.NewLeftToday));
    }

    [Fact]
    public void A_missed_day_spreads_over_the_days_that_remain()
    {
        var exam = Start.AddDays(17); // 3 review days, 14 learning days
        var first = StudyPlanner.Plan(Start, exam, Start, unansweredAtStartOfDay: 140, newDoneToday: 0);
        var afterMissedDay = StudyPlanner.Plan(Start, exam, Start.AddDays(1), unansweredAtStartOfDay: 140, newDoneToday: 0);

        Assert.Equal(10, first.NewQuota);
        Assert.Equal(13, afterMissedDay.LearningDaysLeft);
        Assert.Equal(11, afterMissedDay.NewQuota); // ceil(140 / 13)
    }

    [Fact]
    public void The_review_phase_is_fixed_when_the_goal_is_set_so_it_still_starts_on_time()
    {
        var exam = Start.AddDays(30); // 6 review days, from day 24
        var dayBefore = StudyPlanner.Plan(Start, exam, Start.AddDays(23), unansweredAtStartOfDay: 5, newDoneToday: 0);
        var firstReviewDay = StudyPlanner.Plan(Start, exam, Start.AddDays(24), unansweredAtStartOfDay: 5, newDoneToday: 0);

        Assert.Equal((StudyPhase.Learning, 1, 5), (dayBefore.Phase, dayBefore.LearningDaysLeft, dayBefore.NewQuota));
        Assert.Equal((StudyPhase.Review, 0, 0, 6), (firstReviewDay.Phase, firstReviewDay.NewQuota, firstReviewDay.NewLeftToday, firstReviewDay.DaysLeft));
    }

    [Fact]
    public void The_exam_day_has_no_new_questions()
    {
        var day = StudyPlanner.Plan(Start, Start.AddDays(10), Start.AddDays(10), unansweredAtStartOfDay: 50, newDoneToday: 0);

        Assert.Equal((StudyPhase.ExamDay, 0, 0), (day.Phase, day.DaysLeft, day.NewQuota));
    }

    [Fact]
    public void An_exam_tomorrow_puts_everything_in_today()
    {
        var day = StudyPlanner.Plan(Start, Start.AddDays(1), Start, unansweredAtStartOfDay: 30, newDoneToday: 0);

        Assert.Equal((StudyPhase.Learning, 1, 30), (day.Phase, day.LearningDaysLeft, day.NewQuota));
    }

    [Fact]
    public void A_quota_above_the_daily_cap_warns_and_keeps_the_quota()
    {
        var day = StudyPlanner.Plan(Start, Start.AddDays(3), Start, unansweredAtStartOfDay: 500, newDoneToday: 0);

        Assert.Equal(2, day.LearningDaysLeft); // 3 plan days: 1 review day
        Assert.Equal(250, day.NewQuota);
        Assert.True(day.OverCap);
        Assert.Equal(80, StudyPlanner.DailyCap);
    }

    [Fact]
    public void Nothing_left_to_learn_gives_a_zero_quota()
    {
        var day = StudyPlanner.Plan(Start, Start.AddDays(40), Start, unansweredAtStartOfDay: 0, newDoneToday: 0);

        Assert.Equal((StudyPhase.Learning, 0, 0), (day.Phase, day.NewQuota, day.NewLeftToday));
    }
}
