using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Planning;

public enum StudyGoalDateCheck
{
    Ok,
    NotInFuture,
    TooFar,
}

/// <summary>
/// A learner's target exam and its date (PLN-01, D-120). One active goal per learner and exam; removing a goal sets
/// <see cref="ArchivedAt"/>. Not in schema.sql.
/// </summary>
public class StudyGoal
{
    public const int MaxYearsAhead = 2;

    public long Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public long ExamId { get; set; }

    public Exam? Exam { get; set; }

    /// <summary>The exam day as a Jakarta calendar date.</summary>
    public DateOnly ExamDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ArchivedAt { get; set; }

    /// <summary>Calendar days from <paramref name="today"/> (Jakarta) to the exam: 0 on the exam day, negative after it.</summary>
    public int DaysLeft(DateOnly today) => ExamDate.DayNumber - today.DayNumber;

    /// <summary>An exam date must be after today and at most <see cref="MaxYearsAhead"/> years ahead.</summary>
    public static StudyGoalDateCheck CheckDate(DateOnly examDate, DateOnly today) =>
        examDate <= today ? StudyGoalDateCheck.NotInFuture
        : examDate > today.AddYears(MaxYearsAhead) ? StudyGoalDateCheck.TooFar
        : StudyGoalDateCheck.Ok;
}
