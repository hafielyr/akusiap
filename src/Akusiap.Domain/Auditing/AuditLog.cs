using Akusiap.Domain.Users;

namespace Akusiap.Domain.Auditing;

public class AuditLog
{
    public long Id { get; set; }

    public Guid ActorUserId { get; set; }

    public User? ActorUser { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>JSON (jsonb), optional.</summary>
    public string? Changes { get; set; }

    public DateTime CreatedAt { get; set; }
}
