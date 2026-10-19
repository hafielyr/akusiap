namespace Akusiap.Domain.Content;

public class ExamQuestion
{
    public long ExamId { get; set; }

    public Exam? Exam { get; set; }

    public long QuestionId { get; set; }

    public Question? Question { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Part of the fixed trial set (D-08).</summary>
    public bool IsTrial { get; set; }
}
