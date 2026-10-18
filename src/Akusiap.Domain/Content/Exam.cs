using Akusiap.Domain.Users;

namespace Akusiap.Domain.Content;

public class Exam
{
    public int ExamId { get; set; }

    /// <summary>Null for app (owner-created) exams.</summary>
    public Guid? OwnerUserId { get; set; }

    public User? OwnerUser { get; set; }

    public OwnerKind OwnerKind { get; set; } = OwnerKind.App;

    public int? SubjectId { get; set; }

    public Subject? Subject { get; set; }

    /// <summary>Default rules.</summary>
    public int RulePresetId { get; set; }

    public RulePreset? RulePreset { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    /// <summary>Free for registered users.</summary>
    public bool IsFree { get; set; }

    /// <summary>Rupiah, tax-inclusive. App exams only.</summary>
    public long? PriceIdr { get; set; }

    /// <summary>Access length for a paid exam.</summary>
    public int? AccessDays { get; set; }

    /// <summary>Own-question allowance for buyers of this exam.</summary>
    public int OwnQuestionAllowance { get; set; }

    public ExamStatus Status { get; set; } = ExamStatus.Draft;

    public bool IsShared { get; set; }

    /// <summary>Link token for non-users.</summary>
    public string? ShareToken { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<ExamQuestion> Questions { get; } = [];
}
