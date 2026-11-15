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

    /// <summary>The fixed SKD tryout set the question belongs to (CPNS-07, D-142), 1 to 10; null when in none.</summary>
    public short? TryoutSet { get; set; }
}
