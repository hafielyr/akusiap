using Akusiap.Domain.Users;

namespace Akusiap.Domain.Notifications;

public enum NotificationChannel
{
    Email,
    InApp,
}

public enum NotificationTemplate
{
    Receipt,
    Renewal7d,
    Renewal1d,
    RefundLink,
    ReviewDue,
}

public enum NotificationStatus
{
    Queued,
    Sent,
    Delivered,
    Read,
    Failed,
}

public class Notification
{
    public long Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    public NotificationChannel Channel { get; set; }

    public NotificationTemplate TemplateCode { get; set; }

    /// <summary>"id" (default) or "en".</summary>
    public string Language { get; set; } = Languages.Indonesian;

    /// <summary>JSON (jsonb) with the template values.</summary>
    public string Payload { get; set; } = "{}";

    public NotificationStatus Status { get; set; } = NotificationStatus.Queued;

    /// <summary>Email provider message id.</summary>
    public string? ExternalMessageId { get; set; }

    public DateTime ScheduledAt { get; set; }

    public DateTime? SentAt { get; set; }

    public string? Error { get; set; }
}
