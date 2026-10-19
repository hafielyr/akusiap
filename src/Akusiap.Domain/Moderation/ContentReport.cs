using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Moderation;

public enum ContentReportStatus
{
    Open,
    Fixed,
    TakenDown,
    Rejected,
}

/// <summary>A report about a question, an exam, or both (at least one is required).</summary>
public class ContentReport
{
    public long Id { get; set; }

    /// <summary>Null for guests.</summary>
    public Guid? ReporterUserId { get; set; }

    public User? ReporterUser { get; set; }

    public long? QuestionId { get; set; }

    public Question? Question { get; set; }

    public long? ExamId { get; set; }

    public Exam? Exam { get; set; }

    public string Reason { get; set; } = string.Empty;

    public ContentReportStatus Status { get; set; } = ContentReportStatus.Open;

    public DateTime CreatedAt { get; set; }

    public Guid? ResolvedByUserId { get; set; }

    public User? ResolvedByUser { get; set; }

    public DateTime? ResolvedAt { get; set; }
}
