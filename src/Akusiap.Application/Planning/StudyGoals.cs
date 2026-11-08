using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Domain;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Planning;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Planning;

/// <summary>An active goal as pages show it: <see cref="DaysLeft"/> is 0 on the exam day and negative after it.</summary>
public sealed record StudyGoalView(long Id, long ExamId, string ExamTitle, DateOnly ExamDate, int DaysLeft);

/// <summary>An exam the learner can pick for a goal.</summary>
public sealed record GoalExamOption(long ExamId, string Title);

/// <summary>
/// Exam goals (PLN-01, D-119, D-120): a signed-in learner sets the date of a published catalog exam they can start
/// (trial access is enough). One active goal per exam: setting it again changes the date. Removing archives it.
/// </summary>
public sealed class StudyGoals(IAppDbContext db, IEntitlementService entitlements, ICurrentUser currentUser, IClock clock)
{
    public const string ExamField = "ExamId";
    public const string DateField = "ExamDate";
    public const string NoAccess = "Goals.NoAccess";
    public const string DateNotInFuture = "Goals.DateNotInFuture";
    public const string DateTooFar = "Goals.DateTooFar";

    /// <summary>The learner's active goals, soonest exam first, including any whose date has passed.</summary>
    public async Task<IReadOnlyList<StudyGoalView>> ActiveAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        var today = JakartaTime.Today(clock.UtcNow);
        var rows = await db.StudyGoals.AsNoTracking()
            .Where(g => g.UserId == userId && g.ArchivedAt == null)
            .OrderBy(g => g.ExamDate).ThenBy(g => g.Id)
            .Select(g => new { g.Id, g.ExamId, g.Exam!.Title, g.ExamDate })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return [.. rows.Select(r => new StudyGoalView(r.Id, r.ExamId, r.Title, r.ExamDate, r.ExamDate.DayNumber - today.DayNumber))];
    }

    /// <summary>The active goal for one exam, or null (also null for guests).</summary>
    public async Task<StudyGoalView?> ForExamAsync(long examId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null)
        {
            return null;
        }

        var goals = await ActiveAsync(cancellationToken).ConfigureAwait(false);
        return goals.FirstOrDefault(g => g.ExamId == examId);
    }

    /// <summary>Published catalog exams the learner can start, by title.</summary>
    public async Task<IReadOnlyList<GoalExamOption>> OptionsAsync(CancellationToken cancellationToken)
    {
        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published)
            .OrderBy(e => e.Title)
            .Select(e => new
            {
                e.Title,
                Facts = new ExamAccessFacts(e.Id, e.OwnerUserId, e.OwnerKind, e.Status, e.IsFree, e.IsShared, e.ShareToken, e.Questions.Any(q => q.IsTrial)),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var options = new List<GoalExamOption>();
        foreach (var exam in exams)
        {
            var decision = await entitlements.DecideAsync(exam.Facts, shareToken: null, cancellationToken).ConfigureAwait(false);
            if (decision.CanStart)
            {
                options.Add(new GoalExamOption(exam.Facts.Id, exam.Title));
            }
        }

        return options;
    }

    /// <summary>Sets or moves the goal for <paramref name="examId"/>; returns the goal id.</summary>
    public async Task<Result<long>> SetAsync(long examId, DateOnly examDate, CancellationToken cancellationToken)
    {
        var userId = UserId();
        var now = clock.UtcNow;
        var problems = new List<Problem>();
        switch (StudyGoal.CheckDate(examDate, JakartaTime.Today(now)))
        {
            case StudyGoalDateCheck.NotInFuture:
                problems.Add(new Problem(DateNotInFuture, DateField));
                break;
            case StudyGoalDateCheck.TooFar:
                problems.Add(new Problem(DateTooFar, DateField, StudyGoal.MaxYearsAhead));
                break;
        }

        var isCatalogExam = await db.Exams.AnyAsync(e => e.Id == examId && e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published, cancellationToken).ConfigureAwait(false);
        var access = isCatalogExam ? await entitlements.GetAsync(examId, shareToken: null, cancellationToken).ConfigureAwait(false) : null;
        if (access is null || !access.Decision.CanStart)
        {
            problems.Add(new Problem(NoAccess, ExamField));
        }

        if (problems.Count > 0)
        {
            return Result.Fail<long>(problems);
        }

        var goal = await db.StudyGoals
            .FirstOrDefaultAsync(g => g.UserId == userId && g.ExamId == examId && g.ArchivedAt == null, cancellationToken)
            .ConfigureAwait(false);
        if (goal is null)
        {
            goal = new StudyGoal { UserId = userId, ExamId = examId, CreatedAt = now };
            db.StudyGoals.Add(goal);
        }

        // UpdatedAt marks when the plan for this date began (PLN-02), so only a new date moves it.
        if (goal.Id == 0 || goal.ExamDate != examDate)
        {
            goal.ExamDate = examDate;
            goal.UpdatedAt = now;
        }
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(goal.Id);
    }

    /// <summary>Removes one of the learner's active goals.</summary>
    public async Task<Result<long>> ArchiveAsync(long goalId, CancellationToken cancellationToken)
    {
        var userId = UserId();
        var goal = await db.StudyGoals
            .FirstOrDefaultAsync(g => g.Id == goalId && g.UserId == userId && g.ArchivedAt == null, cancellationToken)
            .ConfigureAwait(false);
        if (goal is null)
        {
            return Result.Fail<long>(Problem.NotFound);
        }

        goal.ArchivedAt = clock.UtcNow;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(goal.Id);
    }

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("Exam goals need a signed-in user.");
}
