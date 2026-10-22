using Akusiap.Domain.Users;

namespace Akusiap.Domain.Content;

public class RulePreset
{
    public long Id { get; set; }

    /// <summary>Null for system presets.</summary>
    public Guid? OwnerUserId { get; set; }

    public User? OwnerUser { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Null means untimed.</summary>
    public int? TimeLimitMinutes { get; set; }

    /// <summary>Null means all questions.</summary>
    public int? QuestionCount { get; set; }

    public bool HintsEnabled { get; set; } = true;

    public bool ShuffleQuestions { get; set; } = true;

    public bool ShuffleOptions { get; set; } = true;

    /// <summary>Null means unlimited.</summary>
    public int? MaxAttempts { get; set; }

    /// <summary>0–100, or null for no pass mark.</summary>
    public int? PassMarkPercent { get; set; }

    public ShowAnswers ShowAnswers { get; set; } = ShowAnswers.AfterSubmit;

    /// <summary>Only one system preset can be the default.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Simulations use the full-screen CAT layout (TRY-01). Not in schema.sql (D-98).</summary>
    public bool CatMode { get; set; }
}
