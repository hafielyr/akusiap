using Akusiap.Domain.Content;
using Akusiap.Domain.Users;

namespace Akusiap.Domain.Sessions;

/// <summary>One practice or exam session.</summary>
public class Attempt
{
    public long Id { get; set; }

    /// <summary>Null for guests: trial takers and shared-exam takers (who must give <see cref="GuestName"/>).</summary>
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Shared-exam takers.</summary>
    public string? GuestName { get; set; }

    /// <summary>Carries a guest trial into a new account.</summary>
    public Guid? GuestToken { get; set; }

    /// <summary>Required for Exam, Trial and Shared; null for wrong-only, review and custom-mix sets.</summary>
    public long? ExamId { get; set; }

    public Exam? Exam { get; set; }

    /// <summary>The class assignment this attempt was started from (GAM-01); its rules are locked.</summary>
    public long? ClassAssignmentId { get; set; }

    public Classes.ClassAssignment? ClassAssignment { get; set; }

    public AttemptSource Source { get; set; }

    public AttemptMode Mode { get; set; }

    public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;

    /// <summary>JSON (jsonb): the rules in force when the attempt started (D-09).</summary>
    public string RulesSnapshot { get; set; } = "{}";

    public DateTime StartedAt { get; set; }

    public DateTime LastSavedAt { get; set; }

    /// <summary>Required once Submitted.</summary>
    public DateTime? SubmittedAt { get; set; }

    public int TimeSpentSeconds { get; set; }

    public int CorrectCount { get; set; }

    public int TotalCount { get; set; }

    /// <summary>0–100 with two decimals.</summary>
    public decimal? ScorePercent { get; set; }

    /// <summary>Null when the rules have no pass mark.</summary>
    public bool? Passed { get; set; }

    public ICollection<AttemptItem> Items { get; } = [];
}
