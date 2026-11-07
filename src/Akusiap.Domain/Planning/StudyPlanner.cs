namespace Akusiap.Domain.Planning;

public enum StudyPhase
{
    /// <summary>New questions every day, plus due cards.</summary>
    Learning,

    /// <summary>The last days before the exam: due cards only, no new questions.</summary>
    Review,

    /// <summary>The exam day itself.</summary>
    ExamDay,
}

/// <summary>
/// One day of a goal's plan. <see cref="NewQuota"/> is fixed for the day: it comes from the questions still unanswered
/// when the day began, so answering today lowers <see cref="NewLeftToday"/> and not the quota.
/// </summary>
public sealed record StudyPlanDay(StudyPhase Phase, int DaysLeft, int LearningDaysLeft, int NewQuota, int NewDoneToday, bool OverCap)
{
    public int NewLeftToday => Math.Max(0, NewQuota - NewDoneToday);
}

/// <summary>
/// The daily plan to an exam date (PLN-02, D-121). The review phase is the last 7 days, or 20% of a plan shorter
/// than 35 days. Its length is fixed from the day the goal was set or last moved, so it can't shrink as the exam
/// gets closer. Each learning day gets ceil(unanswered / learning days left) new questions, recomputed every day,
/// so a missed day spreads over the days that remain.
/// </summary>
public static class StudyPlanner
{
    public const int ReviewDays = 7;
    public const int ShortPlanDays = 35;
    public const int ShortPlanReviewPercent = 20;

    /// <summary>Above this many new questions a day the plan warns that the date is close (and keeps the quota).</summary>
    public const int DailyCap = 80;

    /// <summary>Review-phase length for a plan that starts <paramref name="planDays"/> days before the exam.</summary>
    public static int ReviewPhaseDays(int planDays) =>
        planDays >= ShortPlanDays ? ReviewDays
        : planDays >= 2 ? Math.Max(1, planDays * ShortPlanReviewPercent / 100)
        : 0;

    /// <summary>
    /// How many questions should be answered by now on a straight line over the learning days (PLN-04): none on the
    /// day the goal is set, all of them once the review phase begins.
    /// </summary>
    public static int ExpectedAnswered(DateOnly planStart, DateOnly examDate, DateOnly today, int poolSize)
    {
        var planDays = examDate.DayNumber - planStart.DayNumber;
        var learningDays = planDays - ReviewPhaseDays(planDays);
        if (learningDays <= 0)
        {
            return poolSize;
        }

        var elapsed = Math.Clamp(today.DayNumber - planStart.DayNumber, 0, learningDays);
        return (int)Math.Ceiling((decimal)poolSize * elapsed / learningDays);
    }

    /// <param name="planStart">The Jakarta day the goal was set or its date last moved.</param>
    /// <param name="unansweredAtStartOfDay">Questions in the pool with no answer before today.</param>
    /// <param name="newDoneToday">Questions answered for the first time today.</param>
    public static StudyPlanDay Plan(DateOnly planStart, DateOnly examDate, DateOnly today, int unansweredAtStartOfDay, int newDoneToday)
    {
        var daysLeft = examDate.DayNumber - today.DayNumber;
        if (daysLeft <= 0)
        {
            return new StudyPlanDay(StudyPhase.ExamDay, Math.Max(0, daysLeft), 0, 0, newDoneToday, OverCap: false);
        }

        var reviewDays = ReviewPhaseDays(examDate.DayNumber - planStart.DayNumber);
        var learningDaysLeft = Math.Max(0, daysLeft - reviewDays);
        if (learningDaysLeft == 0)
        {
            return new StudyPlanDay(StudyPhase.Review, daysLeft, 0, 0, newDoneToday, OverCap: false);
        }

        var unanswered = Math.Max(0, unansweredAtStartOfDay);
        var quota = (unanswered + learningDaysLeft - 1) / learningDaysLeft;
        return new StudyPlanDay(StudyPhase.Learning, daysLeft, learningDaysLeft, quota, newDoneToday, quota > DailyCap);
    }
}
