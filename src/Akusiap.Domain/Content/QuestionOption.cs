namespace Akusiap.Domain.Content;

public class QuestionOption
{
    public const int MaxPoints = 5;

    public long Id { get; set; }

    public long QuestionId { get; set; }

    public Question? Question { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Points for picking this option, 0–5 (CPNS-03, D-130): TKP questions score every option. Null for right/wrong
    /// questions. Not in schema.sql.
    /// </summary>
    public int? Points { get; set; }
}
