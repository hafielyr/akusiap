using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Access;

public class ExamPurchase
{
    public int ExamPurchaseId { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public int ExamId { get; set; }

    public Exam? Exam { get; set; }

    public long PricePaidIdr { get; set; }

    public ExamPurchaseStatus Status { get; set; } = ExamPurchaseStatus.PendingPayment;

    /// <summary>Null until paid.</summary>
    public DateTime? PurchasedAt { get; set; }

    /// <summary>Computed from <see cref="Exam.AccessDays"/>.</summary>
    public DateTime AccessExpiresAt { get; set; }
}
