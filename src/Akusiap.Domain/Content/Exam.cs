using Akusiap.Domain.Users;

namespace Akusiap.Domain.Content;

public class Exam
{
    public long Id { get; set; }

    /// <summary>Null exactly when <see cref="OwnerKind"/> is App.</summary>
    public Guid? OwnerUserId { get; set; }

    public User? OwnerUser { get; set; }

    public OwnerKind OwnerKind { get; set; } = OwnerKind.App;

    public long? SubjectId { get; set; }

    public Subject? Subject { get; set; }

    /// <summary>Default rules.</summary>
    public long RulePresetId { get; set; }

    public RulePreset? RulePreset { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Free for registered users.</summary>
    public bool IsFree { get; set; }

    /// <summary>Rupiah, tax-inclusive. Only app exams are sold.</summary>
    public long? PriceIdr { get; set; }

    /// <summary>Access length for a paid exam.</summary>
    public int? AccessDays { get; set; }

    /// <summary>Own-question allowance for buyers of this exam.</summary>
    public int OwnQuestionAllowance { get; set; }

    public ExamStatus Status { get; set; } = ExamStatus.Draft;

    /// <summary>Requires a <see cref="ShareToken"/>.</summary>
    public bool IsShared { get; set; }

    /// <summary>Link token for non-users.</summary>
    public string? ShareToken { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<ExamQuestion> Questions { get; } = [];
}
