using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Gamification;

/// <summary>A badge a user earned (GAM-01, D-95): once per code, for rank badges once per exam, and for weekly rank badges once per exam and week (DRL-02). Not in schema.sql.</summary>
public class UserBadge
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public BadgeCode Code { get; set; }

    /// <summary>The exam a rank badge is for; null for journey milestones.</summary>
    public long? ExamId { get; set; }

    public Exam? Exam { get; set; }

    /// <summary>For a weekly rank badge (DRL-02): the Monday of the week it was won; null otherwise.</summary>
    public DateOnly? WeekStart { get; set; }

    public DateTime AwardedAt { get; set; }
}
