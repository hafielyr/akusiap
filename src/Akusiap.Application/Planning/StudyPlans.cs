using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Application.Sessions;
using Akusiap.Domain;
using Akusiap.Domain.Planning;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Planning;

/// <summary>
/// Today's plan for one goal as the Today page shows it. <see cref="PoolSize"/> counts the exam's questions the learner
/// can use; <see cref="Answered"/> how many of them they have ever answered.
/// </summary>
public sealed record StudyPlanToday(StudyGoalView Goal, StudyPlanDay Day, int PoolSize, int Answered, int DueCards, long? OpenAttemptId)
{
    /// <summary>Nothing left to do today: no new questions left in the quota and no due cards.</summary>
    public bool DoneToday => Day.NewLeftToday == 0 && DueCards == 0 && OpenAttemptId is null;
}

/// <summary>A goal's progress (PLN-04): <see cref="Behind"/> is how many answers short of the straight-line schedule the learner is.</summary>
public sealed record GoalProgressView(StudyPlanToday Plan, int Mastered, int Expected, IReadOnlyList<TopicProgress> Topics)
{
    public int Behind => Math.Max(0, Expected - Plan.Answered);
}

/// <summary>
/// The daily plan for exam goals (PLN-02, D-119, D-121). The pool is the goal exam's questions the learner can use
/// through <see cref="IEntitlementService"/>: all of them with full access, the trial set with trial access. A plan
/// session is a Practice attempt (Source StudyPlan, on the goal's exam) of up to 10 questions: due review cards first
/// (oldest first), then questions never answered, up to what is left of today's quota. The review phase and the exam
/// day offer due cards only.
/// </summary>
public sealed class StudyPlans(
    IAppDbContext db,
    IEntitlementService entitlements,
    ExamQuestionScope questionScope,
    StudyGoals goals,
    StartAttempt startAttempt,
    ICurrentUser currentUser,
    IClock clock,
    IRandomSource random)
{
    public const int SessionSize = 10;
    public const string NothingToday = "Plan.NothingToday";

    /// <summary>Today's plan for every goal whose exam is today or later, soonest exam first.</summary>
    public async Task<IReadOnlyList<StudyPlanToday>> TodayAsync(CancellationToken cancellationToken)
    {
        var plans = new List<StudyPlanToday>();
        foreach (var goal in (await goals.ActiveAsync(cancellationToken).ConfigureAwait(false)).Where(g => g.DaysLeft >= 0))
        {
            plans.Add((await LoadAsync(goal, cancellationToken).ConfigureAwait(false)).Today);
        }

        return plans;
    }

    /// <summary>Opens the goal's unfinished plan session, or starts one from today's plan.</summary>
    public async Task<Result<long>> StartAsync(long goalId, CancellationToken cancellationToken)
    {
        var goal = (await goals.ActiveAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(g => g.Id == goalId && g.DaysLeft >= 0);
        if (goal is null)
        {
            return Result.Fail<long>(Problem.NotFound);
        }

        var (today, pool, due, answered, _) = await LoadAsync(goal, cancellationToken).ConfigureAwait(false);
        if (today.OpenAttemptId is { } open)
        {
            return Result.Ok(open);
        }

        var newQuestions = Math.Min(SessionSize - due.Count, today.Day.NewLeftToday);
        // A due card's question can still be unanswered (a card added without an answer), so leave it out of the new ones.
        var cards = due.Take(SessionSize).ToList();
        var picked = cards
            .Concat(Shuffle(pool.Where(q => !answered.ContainsKey(q)).Except(cards)).Take(Math.Max(0, newQuestions)))
            .ToList();
        if (picked.Count == 0)
        {
            return Result.Fail<long>(new Problem(NothingToday));
        }

        var started = await startAttempt.StartStudyPlanAsync(goal.ExamId, picked, SessionSize, cancellationToken).ConfigureAwait(false);
        return started.Succeeded ? Result.Ok(started.Value!.AttemptId) : Result.Fail<long>(started.Problems);
    }

    /// <summary>
    /// A goal's progress page (PLN-04, D-122): answered and mastered questions overall and per topic (weakest first),
    /// and how far behind a straight line over the learning days the learner is. Past goals work too.
    /// </summary>
    public async Task<Result<GoalProgressView>> ProgressAsync(long goalId, CancellationToken cancellationToken)
    {
        var goal = (await goals.ActiveAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(g => g.Id == goalId);
        if (goal is null)
        {
            return Result.Fail<GoalProgressView>(Problem.NotFound);
        }

        var userId = UserId();
        var (today, pool, _, _, planStart) = await LoadAsync(goal, cancellationToken).ConfigureAwait(false);
        var answers = await db.AttemptItems.AsNoTracking()
            .Where(i => i.Attempt!.UserId == userId && i.IsAnswered && pool.Contains(i.QuestionId))
            .Select(i => new { i.Id, i.QuestionId, i.IsCorrect, At = i.AnsweredAt ?? i.Attempt!.StartedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var lastCorrect = answers.GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.At).ThenByDescending(a => a.Id).First().IsCorrect);
        var cards = await db.ReviewCards.AsNoTracking()
            .Where(c => c.UserId == userId && pool.Contains(c.QuestionId))
            .ToDictionaryAsync(c => c.QuestionId, c => c.State, cancellationToken)
            .ConfigureAwait(false);
        var questions = await db.Questions.AsNoTracking()
            .Where(q => pool.Contains(q.Id))
            .Select(q => new { q.Id, q.TopicId, TopicName = q.Topic!.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        bool IsMastered(long questionId) => Mastery.IsMastered(
            lastCorrect.TryGetValue(questionId, out var correct) ? correct : null,
            cards.TryGetValue(questionId, out var state) ? state : null);
        var topics = Mastery.WeakestFirst(questions
            .GroupBy(q => (q.TopicId, q.TopicName))
            .Select(g => new TopicProgress(g.Key.TopicId, g.Key.TopicName, g.Count(), g.Count(q => lastCorrect.ContainsKey(q.Id)), g.Count(q => IsMastered(q.Id)))));
        var expected = StudyPlanner.ExpectedAnswered(planStart, goal.ExamDate, JakartaTime.Today(clock.UtcNow), pool.Count);
        return Result.Ok(new GoalProgressView(today, topics.Sum(t => t.Mastered), expected, topics));
    }

    private async Task<(StudyPlanToday Today, IReadOnlyList<long> Pool, IReadOnlyList<long> Due, IReadOnlyDictionary<long, DateTime> Answered, DateOnly PlanStart)> LoadAsync(
        StudyGoalView goal, CancellationToken cancellationToken)
    {
        var userId = UserId();
        var now = clock.UtcNow;
        var today = JakartaTime.Today(now);
        var access = await entitlements.GetAsync(goal.ExamId, shareToken: null, cancellationToken).ConfigureAwait(false);
        var pool = access is { Decision.CanStart: true }
            ? (await questionScope.GetQuestionIdsAsync(goal.ExamId, access.Decision.Scope, cancellationToken).ConfigureAwait(false)).ToList()
            : [];

        // When each pool question was first answered, in any attempt or flashcard review.
        var answered = (await db.AttemptItems.AsNoTracking()
            .Where(i => i.Attempt!.UserId == userId && i.IsAnswered && pool.Contains(i.QuestionId))
            .Select(i => new { i.QuestionId, At = i.AnsweredAt ?? i.Attempt!.StartedAt })
            .GroupBy(i => i.QuestionId)
            .Select(g => new { QuestionId = g.Key, First = g.Min(i => i.At) })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(r => r.QuestionId, r => r.First);
        var newDoneToday = answered.Values.Count(at => JakartaTime.Today(at) == today);
        var due = await db.ReviewCards.AsNoTracking()
            .Where(c => c.UserId == userId && c.DueAt <= now && pool.Contains(c.QuestionId))
            .OrderBy(c => c.DueAt).ThenBy(c => c.QuestionId)
            .Select(c => c.QuestionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var open = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.Source == AttemptSource.StudyPlan && a.ExamId == goal.ExamId && a.Status == AttemptStatus.InProgress)
            .OrderByDescending(a => a.Id)
            .Select(a => (long?)a.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var planStart = await db.StudyGoals.AsNoTracking()
            .Where(g => g.Id == goal.Id)
            .Select(g => g.UpdatedAt)
            .SingleAsync(cancellationToken)
            .ConfigureAwait(false);

        var unansweredAtStartOfDay = pool.Count - answered.Count + newDoneToday;
        var startDay = JakartaTime.Today(planStart);
        var day = StudyPlanner.Plan(startDay, goal.ExamDate, today, unansweredAtStartOfDay, newDoneToday);
        return (new StudyPlanToday(goal, day, pool.Count, answered.Count, due.Count, open), pool, due, answered, startDay);
    }

    private List<long> Shuffle(IEnumerable<long> ids)
    {
        var list = ids.ToList();
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = random.Random.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("The study plan needs a signed-in user.");
}
