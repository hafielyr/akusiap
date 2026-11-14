namespace Akusiap.Domain.Users;

/// <summary>
/// A browser the user signed in on (PAY-13, D-141), known by the long-lived device cookie. Signing in again on it updates
/// <see cref="LastSignInAt"/> and clears <see cref="RevokedAt"/>; a subscriber over the device limit loses the device
/// that signed in longest ago, which is then signed out on its next request. Not in schema.sql (like D-120).
/// </summary>
public class UserDevice
{
    public const int LabelMaxLength = 120;

    public long Id { get; set; }

    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>The value of the device cookie.</summary>
    public Guid DeviceKey { get; set; }

    /// <summary>Browser and system, from the User-Agent, for the profile list.</summary>
    public string Label { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime LastSignInAt { get; set; }

    /// <summary>Set when the device signed out or was pushed out by a newer one.</summary>
    public DateTime? RevokedAt { get; set; }

    public bool PushedOut { get; set; }
}
