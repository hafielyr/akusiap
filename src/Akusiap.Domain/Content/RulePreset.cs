using Akusiap.Domain.Users;

namespace Akusiap.Domain.Content;

public class RulePreset
{
    public int RulePresetId { get; set; }

    /// <summary>Null for system presets.</summary>
    public Guid? OwnerUserId { get; set; }

    public User? OwnerUser { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Null means untimed.</summary>
    public int? TimeLimitMinutes { get; set; }

    /// <summary>Null means all questions.</summary>
    public int? QuestionCount { get; set; }

    public bool HintsEnabled { get; set; }

    public bool ShuffleQuestions { get; set; }

    public bool ShuffleOptions { get; set; }

    /// <summary>Null means unlimited.</summary>
    public int? MaxAttempts { get; set; }

    public int? PassMarkPercent { get; set; }

    public ShowAnswers ShowAnswers { get; set; } = ShowAnswers.AfterSubmit;

    public bool IsDefault { get; set; }
}
