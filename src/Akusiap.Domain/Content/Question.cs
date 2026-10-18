using Akusiap.Domain.Users;

namespace Akusiap.Domain.Content;

public class Question
{
    public const int KeyFactMaxLength = 200;
    public const int MinDifficulty = 1;
    public const int MaxDifficulty = 5;

    public int QuestionId { get; set; }

    /// <summary>Null for app (owner-created) questions.</summary>
    public Guid? OwnerUserId { get; set; }

    public User? OwnerUser { get; set; }

    public int TopicId { get; set; }

    public Topic? Topic { get; set; }

    public QuestionType QuestionType { get; set; }

    public string Text { get; set; } = string.Empty;

    public string Explanation { get; set; } = string.Empty;

    /// <summary>One line to memorize (max 200 characters).</summary>
    public string KeyFact { get; set; } = string.Empty;

    public string? Hint { get; set; }

    /// <summary>1 (easy) to 5 (hard).</summary>
    public int Difficulty { get; set; } = MinDifficulty;

    /// <summary>Syllabus reference.</summary>
    public string SourceNote { get; set; } = string.Empty;

    /// <summary>Links variants of the same fact.</summary>
    public Guid? VariantGroupId { get; set; }

    public QuestionStatus Status { get; set; } = QuestionStatus.Draft;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<QuestionOption> Options { get; } = [];
}
