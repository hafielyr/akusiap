using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Gamification;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Gamification;

/// <summary>One row as a viewer sees it: the name is masked when the person opted out, except for themselves.</summary>
public sealed record LeaderboardLine(int Rank, string Name, decimal ScorePercent, int TimeSpentSeconds, DateTime SubmittedAt, bool IsViewer, long AttemptId);

public sealed record LeaderboardView(string Title, long ExamId, int Total, IReadOnlyList<LeaderboardLine> Top, LeaderboardLine? Viewer)
{
    /// <summary>The exam's board settings (DRL-02); the defaults for class boards.</summary>
    public LeaderboardSettings Settings { get; init; } = LeaderboardSettings.Default(ExamId);

    /// <summary>Which board this is. "This week" only exists when the exam resets weekly.</summary>
    public LeaderboardPeriod Period { get; init; }

    /// <summary>When the weekly board starts again (Monday 00:00 WIB, as UTC); null when it never resets.</summary>
    public DateTime? ResetsAt { get; init; }
}

/// <summary>The all-time board, or (for exams that reset weekly) the current Jakarta week from Monday 00:00 WIB.</summary>
public enum LeaderboardPeriod
{
    AllTime,
    ThisWeek,
}

/// <summary>Where an attempt stands on its leaderboard, for the result page and rank badges.</summary>
public sealed record AttemptStanding(int Rank, int Total, long ExamId, long? ClassAssignmentId, long? ClassroomId)
{
    /// <summary>The rank is on the attempt's week board (DRL-02); its badges are given when the week ends.</summary>
    public bool Weekly { get; init; }

    /// <summary>The rank badge this attempt earns now under the exam's settings; null on weekly boards.</summary>
    public BadgeCode? Badge { get; init; }
}

/// <summary>An owner's view of everyone who took their exam (story C-09): users and named guests, every finished attempt.</summary>
public sealed record ParticipantRow(string Name, bool IsGuest, AttemptMode Mode, decimal ScorePercent, int CorrectCount, int TotalCount, int TimeSpentSeconds, DateTime SubmittedAt);

/// <summary>
/// Leaderboards (GAM-01, D-94). Each person counts once, with their <b>first</b> finished Simulation attempt, ranked by
/// score, then time, then who finished first (<see cref="Leaderboard"/>). The public board of a catalog exam counts
/// signed-in attempts with full access (not trials, not class attempts); a class board counts the attempts started from
/// that class assignment. Names of people who chose to hide them show as "Peserta #XXXX" to everyone but themselves;
/// the teacher of a class always sees real names. A catalog exam's board follows its settings (DRL-02, D-99): it can be
/// switched off, count only attempts after a history reset, and reset weekly, where each person's first finished
/// Simulation of the week counts on that week's board.
/// </summary>
public sealed class Leaderboards(IAppDbContext db, ICurrentUser currentUser, IClock clock)
{
    public const int TopSize = 50;

    /// <summary>
    /// The public board of a published app exam, or null when there is none (other exams, or the board is off).
    /// <paramref name="period"/> is ignored for exams that don't reset weekly.
    /// </summary>
    public async Task<LeaderboardView?> ForExamAsync(long examId, LeaderboardPeriod period, CancellationToken cancellationToken)
    {
        var exam = await db.Exams.AsNoTracking()
            .Where(e => e.Id == examId && e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published)
            .Select(e => new { e.Id, e.Title })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (exam is null)
        {
            return null;
        }

        var settings = await SettingsAsync(examId, cancellationToken).ConfigureAwait(false);
        if (!settings.Enabled)
        {
            return null;
        }

        var weekly = settings.WeeklyReset && period == LeaderboardPeriod.ThisWeek;
        var thisWeek = LeaderboardWeek.Of(clock.UtcNow);
        var rows = Leaderboard.Rank(await CandidatesAsync(PublicAttempts(examId, settings, weekly ? thisWeek : null), cancellationToken).ConfigureAwait(false));
        return View(exam.Title, exam.Id, rows, revealNames: false) with
        {
            Settings = settings,
            Period = weekly ? LeaderboardPeriod.ThisWeek : LeaderboardPeriod.AllTime,
            ResetsAt = settings.WeeklyReset ? LeaderboardWeek.BoundsUtc(thisWeek).End : null,
        };
    }

    /// <summary>The board of one class assignment. The teacher sees real names; classmates see masked opt-outs.</summary>
    public async Task<LeaderboardView> ForAssignmentAsync(long assignmentId, string title, long examId, bool viewerIsTeacher, CancellationToken cancellationToken)
    {
        var rows = Leaderboard.Rank(await CandidatesAsync(AssignmentAttempts(assignmentId), cancellationToken).ConfigureAwait(false));
        return View(title, examId, rows, revealNames: viewerIsTeacher);
    }

    /// <summary>Whether the exam's public board is on (DRL-02), for the link on the exam page.</summary>
    public async Task<bool> IsEnabledAsync(long examId, CancellationToken cancellationToken) =>
        (await SettingsAsync(examId, cancellationToken).ConfigureAwait(false)).Enabled;

    /// <summary>The exam's stored settings, or the defaults when an admin never changed them.</summary>
    public async Task<LeaderboardSettings> SettingsAsync(long examId, CancellationToken cancellationToken) =>
        await db.LeaderboardSettings.AsNoTracking().SingleOrDefaultAsync(s => s.ExamId == examId, cancellationToken).ConfigureAwait(false)
        ?? LeaderboardSettings.Default(examId);

    /// <summary>The final ranks of one week's board, for the weekly badges (DRL-02).</summary>
    public async Task<IReadOnlyList<LeaderboardRow>> WeekAsync(LeaderboardSettings settings, DateOnly monday, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Leaderboard.Rank(await CandidatesAsync(PublicAttempts(settings.ExamId, settings, monday), cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The rank of a finished attempt when it is the one that counts for its person; otherwise null.</summary>
    public async Task<AttemptStanding?> StandingAsync(long attemptId, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.AsNoTracking()
            .Where(a => a.Id == attemptId)
            .Select(a => new { a.ExamId, a.ClassAssignmentId, ClassroomId = a.ClassAssignment == null ? (long?)null : a.ClassAssignment.ClassroomId, a.UserId, a.Mode, a.Status, a.Source, a.SubmittedAt, IsApp = a.Exam != null && a.Exam.OwnerKind == OwnerKind.App })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (attempt is null || attempt.ExamId is not { } examId || attempt.UserId is null || attempt.Mode != AttemptMode.Simulation
            || attempt.Status == AttemptStatus.InProgress || attempt.SubmittedAt is not { } submittedAt)
        {
            return null;
        }

        IQueryable<Attempt> scope;
        var settings = LeaderboardSettings.Default(examId);
        var weekly = false;
        if (attempt.ClassAssignmentId is { } assignmentId)
        {
            scope = AssignmentAttempts(assignmentId);
        }
        else if (attempt.IsApp && attempt.Source == AttemptSource.Exam)
        {
            settings = await SettingsAsync(examId, cancellationToken).ConfigureAwait(false);
            if (!settings.Enabled)
            {
                return null;
            }

            weekly = settings.WeeklyReset;
            scope = PublicAttempts(examId, settings, weekly ? LeaderboardWeek.Of(submittedAt) : null);
        }
        else
        {
            return null;
        }

        var rows = Leaderboard.Rank(await CandidatesAsync(scope, cancellationToken).ConfigureAwait(false));
        var mine = rows.FirstOrDefault(r => r.Entry.AttemptId == attemptId);
        return mine is null ? null : new AttemptStanding(mine.Rank, rows.Count, examId, attempt.ClassAssignmentId, attempt.ClassroomId)
        {
            Weekly = weekly,
            Badge = weekly ? null : settings.BadgeFor(mine.Rank, mine.Entry.ScorePercent, rows.Count),
        };
    }

    /// <summary>Everyone who finished the owner's exam, best first (C-09). Null when the viewer doesn't own it.</summary>
    public async Task<IReadOnlyList<ParticipantRow>?> ParticipantsAsync(long examId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        if (userId is null || !await db.Exams.AnyAsync(e => e.Id == examId && e.OwnerUserId == userId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var rows = await db.Attempts.AsNoTracking()
            .Where(a => a.ExamId == examId && a.Status != AttemptStatus.InProgress && a.SubmittedAt != null && a.Mode != AttemptMode.Flashcard)
            .OrderByDescending(a => a.ScorePercent)
            .ThenBy(a => a.TimeSpentSeconds)
            .ThenBy(a => a.SubmittedAt)
            .Take(500)
            .Select(a => new
            {
                UserName = a.User == null ? null : a.User.DisplayName,
                a.GuestName,
                a.Mode,
                a.ScorePercent,
                a.CorrectCount,
                a.TotalCount,
                a.TimeSpentSeconds,
                SubmittedAt = a.SubmittedAt!.Value,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new ParticipantRow(r.UserName ?? r.GuestName ?? "-", r.UserName is null, r.Mode, r.ScorePercent ?? 0m, r.CorrectCount, r.TotalCount, r.TimeSpentSeconds, r.SubmittedAt))];
    }

    /// <summary>How many people count on the exam's all-time board and on this week's board, for the admin page.</summary>
    public async Task<(int AllTime, int ThisWeek)> ParticipantCountsAsync(LeaderboardSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var allTime = await PublicAttempts(settings.ExamId, settings, week: null).Select(a => a.UserId).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);
        var thisWeek = await PublicAttempts(settings.ExamId, settings, LeaderboardWeek.Of(clock.UtcNow)).Select(a => a.UserId).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);
        return (allTime, thisWeek);
    }

    /// <summary>
    /// Counted attempts of the public board: signed-in, full access (Source Exam), outside classes, finished after the
    /// last history reset, and within <paramref name="week"/> when given.
    /// </summary>
    private IQueryable<Attempt> PublicAttempts(long examId, LeaderboardSettings settings, DateOnly? week)
    {
        var attempts = Finished().Where(a => a.ExamId == examId && a.Source == AttemptSource.Exam && a.ClassAssignmentId == null);
        if (settings.CountsSince is { } since)
        {
            attempts = attempts.Where(a => a.SubmittedAt >= since);
        }

        if (week is { } monday)
        {
            var (start, end) = LeaderboardWeek.BoundsUtc(monday);
            attempts = attempts.Where(a => a.SubmittedAt >= start && a.SubmittedAt < end);
        }

        return attempts;
    }

    private IQueryable<Attempt> AssignmentAttempts(long assignmentId) =>
        Finished().Where(a => a.ClassAssignmentId == assignmentId);

    private IQueryable<Attempt> Finished() =>
        db.Attempts.AsNoTracking().Where(a =>
            a.UserId != null && a.Mode == AttemptMode.Simulation && a.Status != AttemptStatus.InProgress && a.SubmittedAt != null);

    /// <summary>Each person's first finished attempt in <paramref name="attempts"/>.</summary>
    private static async Task<List<LeaderboardCandidate>> CandidatesAsync(IQueryable<Attempt> attempts, CancellationToken cancellationToken)
    {
        var firsts = await attempts
            .GroupBy(a => a.UserId)
            .Select(g => g.OrderBy(a => a.SubmittedAt).ThenBy(a => a.Id).Select(a => new
            {
                a.Id,
                UserId = a.UserId!.Value,
                a.User!.DisplayName,
                a.User.HideOnLeaderboard,
                a.ScorePercent,
                a.TimeSpentSeconds,
                SubmittedAt = a.SubmittedAt!.Value,
            }).First())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. firsts.Select(f => new LeaderboardCandidate(f.UserId, f.DisplayName, f.HideOnLeaderboard, f.ScorePercent ?? 0m, f.TimeSpentSeconds, f.SubmittedAt, f.Id))];
    }

    private LeaderboardView View(string title, long examId, IReadOnlyList<LeaderboardRow> rows, bool revealNames)
    {
        var viewerId = currentUser.UserId;
        LeaderboardLine Line(LeaderboardRow r)
        {
            var isViewer = viewerId is { } id && r.Entry.UserId == id;
            var name = revealNames || isViewer ? r.Entry.DisplayName : Leaderboard.PublicName(r.Entry.UserId, r.Entry.DisplayName, r.Entry.HideName);
            return new LeaderboardLine(r.Rank, name, r.Entry.ScorePercent, r.Entry.TimeSpentSeconds, r.Entry.SubmittedAt, isViewer, r.Entry.AttemptId);
        }

        var top = rows.Take(TopSize).Select(Line).ToList();
        var mine = rows.FirstOrDefault(r => viewerId is { } id && r.Entry.UserId == id);
        return new LeaderboardView(title, examId, rows.Count, top, mine is null ? null : Line(mine));
    }
}
