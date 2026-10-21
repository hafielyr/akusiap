using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Gamification;

/// <summary>A badge a user earned (GAM-01, D-95): once per code, and for rank badges once per exam. Not in schema.sql.</summary>
public class UserBadge
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public BadgeCode Code { get; set; }

    /// <summary>The exam a rank badge is for; null for journey milestones.</summary>
    public long? ExamId { get; set; }

    public Exam? Exam { get; set; }

    public DateTime AwardedAt { get; set; }
}
