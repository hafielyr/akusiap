using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Akusiap.Domain.Gamification;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Gamification;

/// <summary>One catalog exam in the admin's leaderboard list.</summary>
public sealed record LeaderboardExamRow(long ExamId, string Title, ExamStatus Status, LeaderboardSettings Settings);

/// <summary>One exam's leaderboard settings with its current participant counts.</summary>
public sealed record LeaderboardAdminDetail(long ExamId, string Title, ExamStatus Status, LeaderboardSettings Settings, int AllTimeParticipants, int ThisWeekParticipants);

public sealed record SaveLeaderboardSettingsRequest(
    long ExamId,
    bool Enabled,
    bool WeeklyReset,
    bool AwardRank1,
    bool AwardTop3,
    bool AwardTop10,
    int? MinScorePercent,
    int? MinParticipants);

/// <summary>
/// Leaderboard management (DRL-02, D-99): admins choose per catalog exam whether the board is on, whether it resets
/// weekly, which rank badges it gives, the minimum score and participants for badges, and can reset its history.
/// </summary>
public sealed class LeaderboardAdmin(IAppDbContext db, Leaderboards leaderboards, ICurrentUser currentUser, IClock clock)
{
    public const string MinScoreField = "MinScorePercent";
    public const string MinScoreLabel = "Admin.Leaderboards.MinScore";
    public const string MinParticipantsField = "MinParticipants";
    public const string MinParticipantsLabel = "Admin.Leaderboards.MinParticipants";
    public const int MaxParticipants = 100_000;

    /// <summary>Every app exam, by title, with its settings (defaults when never changed).</summary>
    public async Task<IReadOnlyList<LeaderboardExamRow>> ListAsync(CancellationToken cancellationToken)
    {
        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.OwnerKind == OwnerKind.App)
            .OrderBy(e => e.Title)
            .Select(e => new { e.Id, e.Title, e.Status })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var ids = exams.Select(e => e.Id).ToList();
        var settings = await db.LeaderboardSettings.AsNoTracking()
            .Where(s => ids.Contains(s.ExamId))
            .ToDictionaryAsync(s => s.ExamId, cancellationToken)
            .ConfigureAwait(false);
        return [.. exams.Select(e => new LeaderboardExamRow(e.Id, e.Title, e.Status, settings.GetValueOrDefault(e.Id) ?? LeaderboardSettings.Default(e.Id)))];
    }

    /// <summary>One app exam's settings and counts; null for other exams.</summary>
    public async Task<LeaderboardAdminDetail?> GetAsync(long examId, CancellationToken cancellationToken)
    {
        var exam = await AppExamAsync(examId, cancellationToken).ConfigureAwait(false);
        if (exam is null)
        {
            return null;
        }

        var settings = await leaderboards.SettingsAsync(examId, cancellationToken).ConfigureAwait(false);
        var (allTime, thisWeek) = await leaderboards.ParticipantCountsAsync(settings, cancellationToken).ConfigureAwait(false);
        return new LeaderboardAdminDetail(exam.Id, exam.Title, exam.Status, settings, allTime, thisWeek);
    }

    public async Task<Result<long>> SaveAsync(SaveLeaderboardSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new List<Problem>();
        if (request.MinScorePercent is < 0 or > 100)
        {
            problems.Add(new Problem("Validation.Range", MinScoreField, MinScoreLabel, 0, 100));
        }

        if (request.MinParticipants is < 1 or > MaxParticipants)
        {
            problems.Add(new Problem("Validation.Range", MinParticipantsField, MinParticipantsLabel, 1, MaxParticipants));
        }

        if (problems.Count > 0)
        {
            return Result.Fail<long>(problems);
        }

        if (await AppExamAsync(request.ExamId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Fail<long>(Problem.NotFound);
        }

        var settings = await TrackedAsync(request.ExamId, cancellationToken).ConfigureAwait(false);
        if (request.WeeklyReset && !settings.WeeklyReset)
        {
            // Weeks before weekly reset was switched on never give weekly badges; the current week is the first.
            settings.WeeklyAwardedThrough = LeaderboardWeek.Of(clock.UtcNow).AddDays(-7);
        }

        settings.Enabled = request.Enabled;
        settings.WeeklyReset = request.WeeklyReset;
        settings.AwardRank1 = request.AwardRank1;
        settings.AwardTop3 = request.AwardTop3;
        settings.AwardTop10 = request.AwardTop10;
        settings.MinScorePercent = request.MinScorePercent;
        settings.MinParticipants = request.MinParticipants;
        await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        return Result.Ok(request.ExamId);
    }

    /// <summary>
    /// Starts the exam's boards over from now: attempts finished before stay in everyone's history but no longer count.
    /// Badges already given are kept.
    /// </summary>
    public async Task<Result<long>> ResetHistoryAsync(long examId, CancellationToken cancellationToken)
    {
        if (await AppExamAsync(examId, cancellationToken).ConfigureAwait(false) is null)
        {
            return Result.Fail<long>(Problem.NotFound);
        }

        var settings = await TrackedAsync(examId, cancellationToken).ConfigureAwait(false);
        settings.CountsSince = clock.UtcNow;
        await SaveAsync(settings, cancellationToken).ConfigureAwait(false);
        return Result.Ok(examId);
    }

    private async Task<LeaderboardSettings> TrackedAsync(long examId, CancellationToken cancellationToken)
    {
        var settings = await db.LeaderboardSettings.SingleOrDefaultAsync(s => s.ExamId == examId, cancellationToken).ConfigureAwait(false);
        if (settings is null)
        {
            settings = LeaderboardSettings.Default(examId);
            db.LeaderboardSettings.Add(settings);
        }

        return settings;
    }

    private async Task SaveAsync(LeaderboardSettings settings, CancellationToken cancellationToken)
    {
        settings.UpdatedAt = clock.UtcNow;
        settings.UpdatedByUserId = currentUser.UserId;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Exam?> AppExamAsync(long examId, CancellationToken cancellationToken) =>
        await db.Exams.AsNoTracking()
            .SingleOrDefaultAsync(e => e.Id == examId && e.OwnerKind == OwnerKind.App, cancellationToken)
            .ConfigureAwait(false);
}
