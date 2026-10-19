using System.Text.Json;
using Akusiap.Application.Abstractions;

namespace Akusiap.Application.Sessions;

/// <summary>Who may use an attempt, and when it must stop (D-09, D-56).</summary>
public static class AttemptAccess
{
    /// <summary>The signed-in owner, or for guest attempts the browser holding the same guest token.</summary>
    public static bool IsOwner(ICurrentUser currentUser, Guid? attemptUserId, Guid? attemptGuestToken, Guid? guestToken)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        return attemptUserId is { } owner
            ? currentUser.UserId == owner
            : attemptGuestToken is not null && attemptGuestToken == guestToken;
    }

    /// <summary>The time limit stored in the rules snapshot, or null when untimed.</summary>
    public static int? TimeLimitMinutes(string rulesSnapshot)
    {
        using var rules = JsonDocument.Parse(rulesSnapshot);
        return rules.RootElement.TryGetProperty("timeLimitMinutes", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : null;
    }

    /// <summary>The server deadline: StartedAt + time limit, or null when untimed (D-09).</summary>
    public static DateTime? Deadline(string rulesSnapshot, DateTime startedAt) =>
        TimeLimitMinutes(rulesSnapshot) is { } minutes ? startedAt.AddMinutes(minutes) : null;
}
