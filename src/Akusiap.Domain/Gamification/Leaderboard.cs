namespace Akusiap.Domain.Gamification;

/// <summary>One person's counted attempt on a leaderboard (GAM-01): their first finished Simulation attempt.</summary>
public sealed record LeaderboardCandidate(Guid UserId, string DisplayName, bool HideName, decimal ScorePercent, int TimeSpentSeconds, DateTime SubmittedAt, long AttemptId);

public sealed record LeaderboardRow(int Rank, LeaderboardCandidate Entry);

/// <summary>
/// Leaderboard order (GAM-01, D-94): higher score first, then less time, then whoever finished first. Rank numbers use
/// standard competition ranking, so two people with the same score and time share a rank ("1, 2, 2, 4").
/// </summary>
public static class Leaderboard
{
    public static IReadOnlyList<LeaderboardRow> Rank(IEnumerable<LeaderboardCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var ordered = candidates
            .OrderByDescending(c => c.ScorePercent)
            .ThenBy(c => c.TimeSpentSeconds)
            .ThenBy(c => c.SubmittedAt)
            .ThenBy(c => c.AttemptId)
            .ToList();
        var rows = new List<LeaderboardRow>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var tied = i > 0 && ordered[i].ScorePercent == ordered[i - 1].ScorePercent && ordered[i].TimeSpentSeconds == ordered[i - 1].TimeSpentSeconds;
            rows.Add(new LeaderboardRow(tied ? rows[i - 1].Rank : i + 1, ordered[i]));
        }

        return rows;
    }

    /// <summary>
    /// The name to show to other people: the display name, or "Peserta #XXXX" (from the user id, stable across pages)
    /// when the person chose to hide it.
    /// </summary>
    public static string PublicName(Guid userId, string displayName, bool hideName) =>
        hideName || string.IsNullOrWhiteSpace(displayName) ? $"Peserta #{MaskedNumber(userId)}" : displayName;

    public static string MaskedNumber(Guid userId) => userId.ToString("N")[..4].ToUpperInvariant();
}
