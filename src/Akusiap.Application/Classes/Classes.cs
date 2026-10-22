using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Application.Gamification;
using Akusiap.Domain.Classes;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Classes;

public sealed record ClassSummary(long Id, string Name, string TeacherName, bool IsTeacher, int MemberCount, int OpenAssignments);

public enum AssignmentState
{
    Upcoming,
    Open,
    Closed,
}

public sealed record AssignmentRow(long Id, long ExamId, string ExamTitle, DateTime OpensAt, DateTime ClosesAt, AssignmentState State, int? TimeLimitMinutes, int? MaxAttempts, int MyAttempts);

public sealed record MemberRow(Guid UserId, string DisplayName, string? Email, DateTime JoinedAt);

public sealed record ClassDetail(long Id, string Name, string InviteCode, string TeacherName, bool IsTeacher, IReadOnlyList<MemberRow> Members, IReadOnlyList<AssignmentRow> Assignments);

/// <summary>A teacher's own exam that can be assigned.</summary>
public sealed record AssignableExam(long Id, string Title, int QuestionCount);

/// <summary>One student's results on an assignment (T-14): attempts, the first (counted) score and time, and the best score.</summary>
public sealed record StudentResult(Guid UserId, string Name, int Attempts, decimal? FirstScore, int? FirstSeconds, decimal? BestScore, bool? Passed);

public sealed record AssignmentResults(AssignmentRow Assignment, IReadOnlyList<StudentResult> Students, LeaderboardView Leaderboard);

/// <summary>
/// Teacher classes (GAM-01; stories T-09 to T-14, D-96). Teachers (and admins) create classes with an invite code;
/// signed-in learners join with the code or link; the teacher assigns one of their own exams with an open window and
/// rules copied at that moment (locked, BR-15); students start it as a Simulation; the teacher sees each student's
/// attempts, first score and time, best score, and the class leaderboard; removed students lose access and can't rejoin.
/// </summary>
public sealed class Classes(IAppDbContext db, ICurrentUser currentUser, IClock clock, IRandomSource random, Leaderboards leaderboards, Badges badges)
{
    public const string TeachersOnly = "Class.TeachersOnly";
    public const string CodeUnknown = "Class.CodeUnknown";
    public const string Removed = "Class.Removed";
    public const string OwnClass = "Class.OwnClass";
    public const string ExamRequired = "Class.ExamRequired";
    public const string WindowInvalid = "Class.WindowInvalid";
    public const string NameField = "Name";
    public const string CodeField = "Code";
    public const string ExamField = "ExamId";
    public const string WindowField = "ClosesAt";

    /// <summary>The classes the viewer teaches and the ones they belong to.</summary>
    public async Task<IReadOnlyList<ClassSummary>> MineAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        var now = clock.UtcNow;
        return await db.Classrooms.AsNoTracking()
            .Where(c => c.TeacherUserId == userId || c.Members.Any(m => m.UserId == userId && m.RemovedAt == null))
            .OrderBy(c => c.Name)
            .Select(c => new ClassSummary(
                c.Id,
                c.Name,
                c.TeacherUser!.DisplayName,
                c.TeacherUserId == userId,
                c.Members.Count(m => m.RemovedAt == null),
                c.Assignments.Count(a => a.OpensAt <= now && a.ClosesAt > now)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<long>> CreateAsync(string? name, CancellationToken cancellationToken)
    {
        var userId = UserId();
        if (!currentUser.IsTeacher)
        {
            return Result.Fail<long>(new Problem(TeachersOnly));
        }

        var problems = new List<Problem>();
        TextRules.RequireName(problems, NameField, "Class.Name", name);
        if (problems.Count > 0)
        {
            return Result.Fail<long>(problems);
        }

        string code;
        do
        {
            code = Classroom.NewInviteCode(random.Random);
        }
        while (await db.Classrooms.AnyAsync(c => c.InviteCode == code, cancellationToken).ConfigureAwait(false));

        var classroom = new Classroom { TeacherUserId = userId, Name = TextRules.Clean(name)!, InviteCode = code, CreatedAt = clock.UtcNow };
        db.Classrooms.Add(classroom);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(classroom.Id);
    }

    /// <summary>Joins the class with this invite code (case and spaces don't matter). Joining twice is fine.</summary>
    public async Task<Result<long>> JoinAsync(string? code, CancellationToken cancellationToken)
    {
        var userId = UserId();
        var normalized = new string((code ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var classroom = await db.Classrooms.Include(c => c.Members.Where(m => m.UserId == userId))
            .SingleOrDefaultAsync(c => c.InviteCode == normalized, cancellationToken)
            .ConfigureAwait(false);
        if (classroom is null)
        {
            return Result.Fail<long>(new Problem(CodeUnknown, CodeField));
        }

        if (classroom.TeacherUserId == userId)
        {
            return Result.Fail<long>(new Problem(OwnClass, CodeField));
        }

        if (classroom.Members.SingleOrDefault() is { } member)
        {
            return member.RemovedAt is null ? Result.Ok(classroom.Id) : Result.Fail<long>(new Problem(Removed, CodeField));
        }

        classroom.Members.Add(new ClassMember { UserId = userId, JoinedAt = clock.UtcNow });
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await badges.AwardAsync(userId, attemptId: null, cancellationToken).ConfigureAwait(false);
        return Result.Ok(classroom.Id);
    }

    /// <summary>The class for its teacher or a current member; null for anyone else.</summary>
    public async Task<ClassDetail?> GetAsync(long classId, CancellationToken cancellationToken)
    {
        var userId = UserId();
        var classroom = await db.Classrooms.AsNoTracking()
            .Where(c => c.Id == classId && (c.TeacherUserId == userId || c.Members.Any(m => m.UserId == userId && m.RemovedAt == null)))
            .Select(c => new { c.Id, c.Name, c.InviteCode, TeacherName = c.TeacherUser!.DisplayName, IsTeacher = c.TeacherUserId == userId })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (classroom is null)
        {
            return null;
        }

        var members = classroom.IsTeacher
            ? await db.ClassMembers.AsNoTracking()
                .Where(m => m.ClassroomId == classId && m.RemovedAt == null)
                .OrderBy(m => m.User!.DisplayName)
                .Select(m => new MemberRow(m.UserId, m.User!.DisplayName, m.User.Email, m.JoinedAt))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false)
            : [];
        return new ClassDetail(classroom.Id, classroom.Name, classroom.InviteCode, classroom.TeacherName, classroom.IsTeacher, members, await AssignmentsAsync(classId, userId, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>The teacher's own exams (not taken down) that can be assigned.</summary>
    public async Task<IReadOnlyList<AssignableExam>> AssignableExamsAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        return await db.Exams.AsNoTracking()
            .Where(e => e.OwnerUserId == userId && e.Status != ExamStatus.TakenDown)
            .OrderBy(e => e.Title)
            .Select(e => new AssignableExam(e.Id, e.Title, e.Questions.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Assigns one of the teacher's exams with an open window. The exam's rules are copied and locked.</summary>
    public async Task<Result<long>> AssignAsync(long classId, long? examId, DateTime? opensAt, DateTime? closesAt, CancellationToken cancellationToken)
    {
        var userId = UserId();
        if (!await db.Classrooms.AnyAsync(c => c.Id == classId && c.TeacherUserId == userId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<long>(Problem.NotFound);
        }

        var exam = examId is { } id
            ? await db.Exams.Include(e => e.RulePreset).SingleOrDefaultAsync(e => e.Id == id && e.OwnerUserId == userId && e.Status != ExamStatus.TakenDown, cancellationToken).ConfigureAwait(false)
            : null;
        var problems = new List<Problem>();
        if (exam is null)
        {
            problems.Add(new Problem(ExamRequired, ExamField));
        }

        if (opensAt is null || closesAt is null || closesAt <= opensAt || closesAt <= clock.UtcNow)
        {
            problems.Add(new Problem(WindowInvalid, WindowField));
        }

        if (problems.Count > 0)
        {
            return Result.Fail<long>(problems);
        }

        var source = exam!.RulePreset!;
        var locked = new RulePreset
        {
            OwnerUserId = userId,
            Name = exam.Title,
            TimeLimitMinutes = source.TimeLimitMinutes,
            QuestionCount = source.QuestionCount,
            HintsEnabled = source.HintsEnabled,
            ShuffleQuestions = source.ShuffleQuestions,
            ShuffleOptions = source.ShuffleOptions,
            MaxAttempts = source.MaxAttempts,
            PassMarkPercent = source.PassMarkPercent,
            ShowAnswers = source.ShowAnswers,
        };
        var assignment = new ClassAssignment { ClassroomId = classId, ExamId = exam.Id, RulePreset = locked, OpensAt = opensAt!.Value, ClosesAt = closesAt!.Value };
        db.ClassAssignments.Add(assignment);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(assignment.Id);
    }

    /// <summary>Removes a student (T-13). Their past attempts stay; they lose access and can't rejoin with the code.</summary>
    public async Task<bool> RemoveMemberAsync(long classId, Guid studentId, CancellationToken cancellationToken)
    {
        var userId = UserId();
        var member = await db.ClassMembers
            .SingleOrDefaultAsync(m => m.ClassroomId == classId && m.UserId == studentId && m.RemovedAt == null && m.Classroom!.TeacherUserId == userId, cancellationToken)
            .ConfigureAwait(false);
        if (member is null)
        {
            return false;
        }

        member.RemovedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>The results of one assignment: every current student (T-14) for the teacher, and the class leaderboard.</summary>
    public async Task<AssignmentResults?> ResultsAsync(long classId, long assignmentId, CancellationToken cancellationToken)
    {
        var detail = await GetAsync(classId, cancellationToken).ConfigureAwait(false);
        var assignment = detail?.Assignments.SingleOrDefault(a => a.Id == assignmentId);
        if (detail is null || assignment is null)
        {
            return null;
        }

        var board = await leaderboards.ForAssignmentAsync(assignmentId, assignment.ExamTitle, assignment.ExamId, detail.IsTeacher, cancellationToken).ConfigureAwait(false);
        if (!detail.IsTeacher)
        {
            return new AssignmentResults(assignment, [], board);
        }

        var attempts = await db.Attempts.AsNoTracking()
            .Where(a => a.ClassAssignmentId == assignmentId && a.Status != AttemptStatus.InProgress && a.SubmittedAt != null && a.UserId != null)
            .Select(a => new { UserId = a.UserId!.Value, a.Id, a.ScorePercent, a.TimeSpentSeconds, a.SubmittedAt, a.Passed })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var students = detail.Members.Select(m =>
        {
            var mine = attempts.Where(a => a.UserId == m.UserId).OrderBy(a => a.SubmittedAt).ThenBy(a => a.Id).ToList();
            var first = mine.FirstOrDefault();
            return new StudentResult(m.UserId, m.DisplayName, mine.Count, first?.ScorePercent, first?.TimeSpentSeconds, mine.Max(a => a.ScorePercent), first?.Passed);
        }).ToList();
        return new AssignmentResults(assignment, students, board);
    }

    private async Task<List<AssignmentRow>> AssignmentsAsync(long classId, Guid userId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var rows = await db.ClassAssignments.AsNoTracking()
            .Where(a => a.ClassroomId == classId)
            .OrderByDescending(a => a.OpensAt)
            .Select(a => new
            {
                a.Id,
                a.ExamId,
                a.Exam!.Title,
                a.OpensAt,
                a.ClosesAt,
                a.RulePreset!.TimeLimitMinutes,
                a.RulePreset.MaxAttempts,
                Mine = db.Attempts.Count(t => t.ClassAssignmentId == a.Id && t.UserId == userId),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new AssignmentRow(
            r.Id,
            r.ExamId,
            r.Title,
            r.OpensAt,
            r.ClosesAt,
            now < r.OpensAt ? AssignmentState.Upcoming : now < r.ClosesAt ? AssignmentState.Open : AssignmentState.Closed,
            r.TimeLimitMinutes,
            r.MaxAttempts,
            r.Mine))];
    }

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("Classes need a signed-in user.");
}
