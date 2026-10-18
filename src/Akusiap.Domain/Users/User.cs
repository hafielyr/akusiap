using Microsoft.AspNetCore.Identity;

namespace Akusiap.Domain.Users;

/// <summary>
/// ERD <c>User</c>. Identity supplies Id (UserId), Email, PhoneNumber, EmailConfirmed and PasswordHash.
/// </summary>
public class User : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>"id" (default) or "en".</summary>
    public string PreferredLanguage { get; set; } = Languages.Indonesian;

    /// <summary>Consent for WhatsApp messages.</summary>
    public bool WhatsAppOptIn { get; set; }

    /// <summary>Users are deactivated instead of deleted.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime? DeactivatedAt { get; set; }

    public DateTime CreatedAt { get; set; }
}
