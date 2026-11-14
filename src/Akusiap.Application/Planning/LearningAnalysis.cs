using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Domain.Access;
using Akusiap.Domain.Planning;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Planning;

/// <summary>
/// What the learner's tests say about one exam (ANL-01, D-137): every topic and SKD subtest weakest first, from all the
/// learner's answers to the exam's questions they can use (in any attempt, drill or flashcard set), and the plan built
/// from them.
/// </summary>
/// <param name="TrialOnly">The learner has the trial set only; <see cref="PoolSize"/> counts those questions.</param>
/// <param name="CardsInDeck">Due cards can be reviewed in the flashcard deck (full access); otherwise through the goal's daily plan.</param>
/// <param name="LastActivity">The learner's latest answer in the exam, if any.</param>
public sealed record ExamAnalysis(
    long ExamId,
    string ExamTitle,
    bool TrialOnly,
    int PoolSize,
    int Tried,
    IReadOnlyList<TopicStanding> Topics,
    IReadOnlyList<SubtestStanding> Subtests,
    IReadOnlyList<PlanStep> Steps,
    long? GoalId,
    bool CardsInDeck,
    DateTime? LastActivity)
{
    public int TriedPercent => WeakAreas.Percent(Tried, PoolSize);

    /// <summary>Rated topics that are weak, weakest first.</summary>
    public IEnumerable<TopicStanding> WeakTopics => Topics.Where(t => t.Strength == Strength.Weak);
}

/// <summary>
/// The learner's analysis per exam (ANL-01, ANL-02, D-137). Exams come from the learner's own attempts and active goals;
/// only exams they can still start are analysed, over the questions their access allows (the trial set on a trial).
/// </summary>
public sealed class LearningAnalysis(
    IAppDbContext db,
    IEntitlementService entitlements,
    ExamQuestionScope questionScope,
    StudyGoals goals,
    ICurrentUser currentUser,
    IClock clock)
{
    /// <summary>Exams on the analysis page, most recently practised first.</summary>
    public const int MaxExams = 6;

    public async Task<IReadOnlyList<ExamAnalysis>> AllAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return [];
        }

        var practised = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.ExamId != null)
            .GroupBy(a => a.ExamId!.Value)
            .Select(g => new { ExamId = g.Key, Last = g.Max(a => a.LastSavedAt) })
            .OrderByDescending(g => g.Last)
            .Take(MaxExams * 2)
            .Select(g => g.ExamId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var goalExams = (await goals.ActiveAsync(cancellationToken).ConfigureAwait(false)).Select(g => g.ExamId);

        var result = new List<ExamAnalysis>();
        foreach (var examId in goalExams.Concat(practised).Distinct())
        {
            if (await ForExamAsync(examId, cancellationToken).ConfigureAwait(false) is { } analysis)
            {
                result.Add(analysis);
            }
        }

        return [.. result
            .OrderByDescending(a => a.GoalId is not null)
            .ThenByDescending(a => a.LastActivity ?? DateTime.MinValue)
            .Take(MaxExams)];
    }

    /// <summary>Null for guests, unknown exams and exams the learner can no longer start.</summary>
    public async Task<ExamAnalysis?> ForExamAsync(long examId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var access = await entitlements.GetAsync(examId, shareToken: null, cancellationToken).ConfigureAwait(false);
        if (access is not { Decision.CanStart: true })
        {
            return null;
        }

        var poolIds = (await questionScope.GetQuestionIdsAsync(examId, access.Decision.Scope, cancellationToken).ConfigureAwait(false)).ToList();
        if (poolIds.Count == 0)
        {
            return null;
        }

        var now = clock.UtcNow;
        var title = await db.Exams.AsNoTracking().Where(e => e.Id == examId).Select(e => e.Title).SingleAsync(cancellationToken).ConfigureAwait(false);
        var pool = await db.Questions.AsNoTracking()
            .Where(q => poolIds.Contains(q.Id))
            .Select(q => new PoolQuestion(q.Id, q.TopicId, q.Topic!.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Answered items anywhere, plus blanks in finished attempts (they count as wrong, D-02).
        var answers = await db.AttemptItems.AsNoTracking()
            .Where(i => i.Attempt!.UserId == userId && poolIds.Contains(i.QuestionId)
                && (i.IsAnswered || i.Attempt.Status != AttemptStatus.InProgress))
            .Select(i => new QuestionAnswer(i.QuestionId, i.IsCorrect, i.AnsweredAt ?? i.Attempt!.SubmittedAt ?? i.Attempt.StartedAt, i.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var due = await db.ReviewCards.AsNoTracking()
            .CountAsync(c => c.UserId == userId && c.DueAt <= now && poolIds.Contains(c.QuestionId), cancellationToken)
            .ConfigureAwait(false);
        var lastSimulation = await db.Attempts.AsNoTracking()
            .Where(a => a.UserId == userId && a.ExamId == examId && a.Mode == AttemptMode.Simulation && a.Status != AttemptStatus.InProgress)
            .MaxAsync(a => a.SubmittedAt, cancellationToken)
            .ConfigureAwait(false);
        var goal = await goals.ForExamAsync(examId, cancellationToken).ConfigureAwait(false);

        var trialOnly = access.Decision.Scope == QuestionScope.TrialOnly;
        var cardsInDeck = !trialOnly;

        // On a trial, due cards are reviewed in the goal's daily plan (PLN-03); the deck holds full-access exams only (D-82).
        var reviewable = cardsInDeck || goal is not null ? due : 0;
        var topics = WeakAreas.Topics(pool, answers);
        return new ExamAnalysis(
            examId,
            title,
            trialOnly,
            pool.Count,
            topics.Sum(t => t.Tried),
            topics,
            WeakAreas.Subtests(topics),
            WeakAreas.Plan(topics, reviewable, trialOnly, goal is not null, lastSimulation, now),
            goal?.Id,
            cardsInDeck,
            answers.Count == 0 ? null : answers.Max(a => a.At));
    }
}
