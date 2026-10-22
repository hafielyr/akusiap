using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Gamification;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Gamification;

/// <summary>One row as a viewer sees it: the name is masked when the person opted out, except for themselves.</summary>
public sealed record LeaderboardLine(int Rank, string Name, decimal ScorePercent, int TimeSpentSeconds, DateTime SubmittedAt, bool IsViewer, long AttemptId);

public sealed record LeaderboardView(string Title, long ExamId, int Total, IReadOnlyList<LeaderboardLine> Top, LeaderboardLine? Viewer);

/// <summary>Where an attempt stands on its leaderboard, for the result page.</summary>
public sealed record AttemptStanding(int Rank, int Total, long ExamId, long? ClassAssignmentId, long? ClassroomId);

/// <summary>An owner's view of everyone who took their exam (story C-09): users and named guests, every finished attempt.</summary>
public sealed record ParticipantRow(string Name, bool IsGuest, AttemptMode Mode, decimal ScorePercent, int CorrectCount, int TotalCount, int TimeSpentSeconds, DateTime SubmittedAt);

/// <summary>
/// Leaderboards (GAM-01, D-94). Each person counts once, with their <b>first</b> finished Simulation attempt, ranked by
/// score, then time, then who finished first (<see cref="Leaderboard"/>). The public board of a catalog exam counts
/// signed-in attempts with full access (not trials, not class attempts); a class board counts the attempts started from
/// that class assignment. Names of people who chose to hide them show as "Peserta #XXXX" to everyone but themselves;
/// the teacher of a class always sees real names.
/// </summary>
public sealed class Leaderboards(IAppDbContext db, ICurrentUser currentUser)
{
    public const int TopSize = 50;

    /// <summary>The public board of a published app exam, or null for other exams.</summary>
    public async Task<LeaderboardView?> ForExamAsync(long examId, CancellationToken cancellationToken)
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

        var rows = Leaderboard.Rank(await CandidatesAsync(PublicAttempts(examId), cancellationToken).ConfigureAwait(false));
        return View(exam.Title, exam.Id, rows, revealNames: false);
    }

    /// <summary>The board of one class assignment. The teacher sees real names; classmates see masked opt-outs.</summary>
    public async Task<LeaderboardView> ForAssignmentAsync(long assignmentId, string title, long examId, bool viewerIsTeacher, CancellationToken cancellationToken)
    {
        var rows = Leaderboard.Rank(await CandidatesAsync(AssignmentAttempts(assignmentId), cancellationToken).ConfigureAwait(false));
        return View(title, examId, rows, revealNames: viewerIsTeacher);
    }

    /// <summary>The rank of a finished attempt when it is the one that counts for its person; otherwise null.</summary>
    public async Task<AttemptStanding?> StandingAsync(long attemptId, CancellationToken cancellationToken)
    {
        var attempt = await db.Attempts.AsNoTracking()
            .Where(a => a.Id == attemptId)
            .Select(a => new { a.ExamId, a.ClassAssignmentId, ClassroomId = a.ClassAssignment == null ? (long?)null : a.ClassAssignment.ClassroomId, a.UserId, a.Mode, a.Status, a.Source, IsApp = a.Exam != null && a.Exam.OwnerKind == OwnerKind.App })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (attempt is null || attempt.ExamId is not { } examId || attempt.UserId is null || attempt.Mode != AttemptMode.Simulation
            || attempt.Status == AttemptStatus.InProgress)
        {
            return null;
        }

        IQueryable<Attempt> scope;
        if (attempt.ClassAssignmentId is { } assignmentId)
        {
            scope = AssignmentAttempts(assignmentId);
        }
        else if (attempt.IsApp && attempt.Source == AttemptSource.Exam)
        {
            scope = PublicAttempts(examId);
        }
        else
        {
            return null;
        }

        var rows = Leaderboard.Rank(await CandidatesAsync(scope, cancellationToken).ConfigureAwait(false));
        var mine = rows.FirstOrDefault(r => r.Entry.AttemptId == attemptId);
        return mine is null ? null : new AttemptStanding(mine.Rank, rows.Count, examId, attempt.ClassAssignmentId, attempt.ClassroomId);
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

    /// <summary>Counted attempts of the public board: signed-in, full access (Source Exam), outside classes.</summary>
    private IQueryable<Attempt> PublicAttempts(long examId) =>
        Finished().Where(a => a.ExamId == examId && a.Source == AttemptSource.Exam && a.ClassAssignmentId == null);

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
