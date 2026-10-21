namespace Akusiap.Domain.Gamification;

/// <summary>Badges (GAM-01, D-95). Rank badges belong to one exam; the others are milestones of the learner's own journey.</summary>
public enum BadgeCode
{
    // Rank on an exam leaderboard, at the moment the counted attempt finished.
    Rank1,
    Top3,
    Top10,

    // Learning journey milestones.
    FirstSteps,
    Questions100,
    Questions500,
    Questions1000,
    Sharpshooter,
    PassedSimulation,
    Streak3,
    Streak7,
    Streak30,
    Flashcards50,
    Flashcards250,
    ClassJoined,
}

/// <summary>What the learner has done so far, counted from finished attempts, reviews and classes.</summary>
public sealed record JourneyStats(
    int FinishedAttempts,
    int AnsweredQuestions,
    int CorrectAnswers,
    decimal BestScorePercent,
    bool PassedSimulation,
    int FlashcardReviews,
    int LongestStreak,
    int ClassesJoined);

public sealed record LevelProgress(int Level, int Xp, int XpIntoLevel, int XpForNextLevel)
{
    public int Percent => XpForNextLevel == 0 ? 100 : (int)Math.Round(100.0 * XpIntoLevel / XpForNextLevel);
}

public sealed record StreakInfo(int Current, int Longest, bool ActiveToday);

/// <summary>
/// The learning journey (GAM-01, D-95): XP from what the learner does, levels, streaks in Jakarta days and milestone
/// badges. Pure rules; the application layer counts the inputs.
/// </summary>
public static class Journey
{
    public const int XpPerCorrectAnswer = 10;
    public const int XpPerFinishedAttempt = 20;
    public const int XpPerFlashcardReview = 2;
    public const int XpPerBadge = 50;

    public static int Xp(JourneyStats stats, int badges)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return (stats.CorrectAnswers * XpPerCorrectAnswer) + (stats.FinishedAttempts * XpPerFinishedAttempt)
            + (stats.FlashcardReviews * XpPerFlashcardReview) + (badges * XpPerBadge);
    }

    /// <summary>Total XP needed to reach <paramref name="level"/>: 0, 100, 300, 600, 1000, … (100 more each level).</summary>
    public static int XpForLevel(int level) => level <= 1 ? 0 : 50 * level * (level - 1);

    public static LevelProgress Level(int xp)
    {
        var level = 1;
        while (XpForLevel(level + 1) <= xp)
        {
            level++;
        }

        var start = XpForLevel(level);
        return new LevelProgress(level, xp, xp - start, XpForLevel(level + 1) - start);
    }

    /// <summary>
    /// Streaks over the Jakarta days with any learning activity. The current streak still counts when today has no
    /// activity yet but yesterday had (it breaks only after a whole missed day).
    /// </summary>
    public static StreakInfo Streak(IEnumerable<DateOnly> activeDays, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(activeDays);
        var days = activeDays.Where(d => d <= today).Distinct().Order().ToList();
        var longest = 0;
        var run = 0;
        DateOnly? previous = null;
        foreach (var day in days)
        {
            run = previous is { } p && p.AddDays(1) == day ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = day;
        }

        var activeToday = days.Count > 0 && days[^1] == today;
        var current = days.Count > 0 && (days[^1] == today || days[^1] == today.AddDays(-1)) ? run : 0;
        return new StreakInfo(current, longest, activeToday);
    }

    /// <summary>The milestone badges these stats have earned.</summary>
    public static IReadOnlyList<BadgeCode> EarnedMilestones(JourneyStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        var earned = new List<BadgeCode>();
        void Add(bool condition, BadgeCode code)
        {
            if (condition)
            {
                earned.Add(code);
            }
        }

        Add(stats.FinishedAttempts >= 1, BadgeCode.FirstSteps);
        Add(stats.AnsweredQuestions >= 100, BadgeCode.Questions100);
        Add(stats.AnsweredQuestions >= 500, BadgeCode.Questions500);
        Add(stats.AnsweredQuestions >= 1000, BadgeCode.Questions1000);
        Add(stats.BestScorePercent >= 90m, BadgeCode.Sharpshooter);
        Add(stats.PassedSimulation, BadgeCode.PassedSimulation);
        Add(stats.LongestStreak >= 3, BadgeCode.Streak3);
        Add(stats.LongestStreak >= 7, BadgeCode.Streak7);
        Add(stats.LongestStreak >= 30, BadgeCode.Streak30);
        Add(stats.FlashcardReviews >= 50, BadgeCode.Flashcards50);
        Add(stats.FlashcardReviews >= 250, BadgeCode.Flashcards250);
        Add(stats.ClassesJoined >= 1, BadgeCode.ClassJoined);
        return earned;
    }

    /// <summary>The rank badge for a place on an exam leaderboard, or null outside the top 10.</summary>
    public static BadgeCode? RankBadge(int rank) => rank switch
    {
        1 => BadgeCode.Rank1,
        <= 3 and > 0 => BadgeCode.Top3,
        <= 10 and > 0 => BadgeCode.Top10,
        _ => null,
    };

    public static bool IsRankBadge(BadgeCode code) => code is BadgeCode.Rank1 or BadgeCode.Top3 or BadgeCode.Top10;
}
