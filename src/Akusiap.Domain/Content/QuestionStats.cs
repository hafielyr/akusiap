namespace Akusiap.Domain.Content;

/// <summary>
/// How learners did on one question over all finished sessions (DIF-01, D-140): one row per question, counted when a
/// session ends. Owner columns "true", "false" and "marked" are <see cref="CorrectCount"/>, <see cref="WrongCount"/> and
/// <see cref="MarkedCount"/>. Not in schema.sql (like D-120).
/// </summary>
public class QuestionStats
{
    public long Id { get; set; }

    public long QuestionId { get; set; }

    public Question? Question { get; set; }

    /// <summary>Answered right in a finished session.</summary>
    public int CorrectCount { get; set; }

    /// <summary>Answered wrong in a finished session (blanks are not counted).</summary>
    public int WrongCount { get; set; }

    /// <summary>Marked "ditandai"/"ragu-ragu" at any point in a finished session, even if answered or unmarked later.</summary>
    public int MarkedCount { get; set; }

    public DateTime UpdatedAt { get; set; }

    public MeasuredDifficulty? Difficulty => QuestionDifficulty.Of(CorrectCount, WrongCount, MarkedCount);
}
