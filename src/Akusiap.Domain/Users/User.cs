using Microsoft.AspNetCore.Identity;

namespace Akusiap.Domain.Users;

/// <summary>
/// Table <c>users</c>. Identity supplies Id, Email, PhoneNumber (+62 format), EmailConfirmed and PasswordHash
/// (null for Google-only sign-in).
/// </summary>
public class User : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>"id" (default) or "en".</summary>
    public string PreferredLanguage { get; set; } = Languages.Indonesian;

    /// <summary>Users are deactivated instead of deleted.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>Required when <see cref="IsActive"/> is false.</summary>
    public DateTime? DeactivatedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Show this user as "Peserta #XXXX" on public leaderboards (GAM-01). Not in schema.sql (D-94).</summary>
    public bool HideOnLeaderboard { get; set; }
}
