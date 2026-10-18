using Akusiap.Domain.Users;

namespace Akusiap.Domain.Auditing;

public class AuditLog
{
    public int AuditLogId { get; set; }

    public Guid ActorUserId { get; set; }

    public User? ActorUser { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityName { get; set; } = string.Empty;

    public string EntityId { get; set; } = string.Empty;

    /// <summary>JSON, stored as jsonb.</summary>
    public string ChangesJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
}
