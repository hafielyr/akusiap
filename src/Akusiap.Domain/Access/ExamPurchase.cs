using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Access;

/// <summary>A single exam bought by a user. At most one live (not Abandoned or Refunded) row per user and exam; buying again extends it.</summary>
public class ExamPurchase
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public long ExamId { get; set; }

    public Exam? Exam { get; set; }

    public long PricePaidIdr { get; set; }

    public ExamPurchaseStatus Status { get; set; } = ExamPurchaseStatus.AwaitingPayment;

    /// <summary>Set when paid. Required while Active.</summary>
    public DateTime? PurchasedAt { get; set; }

    /// <summary>From <see cref="Exam.AccessDays"/>. Required while Active.</summary>
    public DateTime? AccessExpiresAt { get; set; }
}
