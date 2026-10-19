using Akusiap.Domain.Content;

namespace Akusiap.Domain.Sessions;

/// <summary>One question within an attempt. Position and question are each unique per attempt.</summary>
public class AttemptItem
{
    public long Id { get; set; }

    public long AttemptId { get; set; }

    public Attempt? Attempt { get; set; }

    public long QuestionId { get; set; }

    public Question? Question { get; set; }

    public int Position { get; set; }

    /// <summary>JSON (jsonb): shuffled option ids.</summary>
    public string? OptionOrder { get; set; }

    public bool IsAnswered { get; set; }

    public bool IsCorrect { get; set; }

    public bool IsFlagged { get; set; }

    public bool HintUsed { get; set; }

    public int TimeSpentSeconds { get; set; }

    public DateTime? AnsweredAt { get; set; }

    public ICollection<AttemptItemOption> SelectedOptions { get; } = [];
}
