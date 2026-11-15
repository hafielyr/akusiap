using Akusiap.Domain.Sessions;

namespace Akusiap.Domain.Planning;

/// <summary>How well a learner does on a topic or subtest right now (ANL-01, D-137).</summary>
public enum Strength
{
    /// <summary>Fewer than <see cref="WeakAreas.MinTried"/> questions tried: too early to say.</summary>
    NotEnough,
    Weak,
    Fair,
    Strong,
}

/// <summary>One question of the exam the learner can use, with its topic.</summary>
public sealed record PoolQuestion(long QuestionId, long TopicId, string Topic);

/// <summary>One answer to a pool question (a blank in a finished attempt counts as wrong, D-02).</summary>
public sealed record QuestionAnswer(long QuestionId, bool IsCorrect, DateTime At, long Order = 0);

/// <summary>
/// A topic's standing: questions it has, how many the learner tried, and how many of those they now answer right
/// (their latest answer to each question).
/// </summary>
public sealed record TopicStanding(long TopicId, string Topic, int Questions, int Tried, int RightNow)
{
    public int Percent => WeakAreas.Percent(RightNow, Tried);

    public Strength Strength => WeakAreas.StrengthOf(Tried, RightNow);
}

/// <summary>The same for one SKD subtest (TWK, TIU, TKP), summed over its topics.</summary>
public sealed record SubtestStanding(SkdSubtest Subtest, string Code, int Questions, int Tried, int RightNow)
{
    public int Percent => WeakAreas.Percent(RightNow, Tried);

    public Strength Strength => WeakAreas.StrengthOf(Tried, RightNow);
}

/// <summary>What one step of the learning plan asks for.</summary>
public enum PlanStepKind
{
    /// <summary>Review the flashcards that are due (Count = due cards).</summary>
    ReviewCards,

    /// <summary>Practise a weak topic (Count = questions to take).</summary>
    PractiseTopic,

    /// <summary>Try a topic not touched yet (Count = questions to take).</summary>
    TryTopic,

    /// <summary>A full simulation of the exam, to check progress under exam conditions.</summary>
    Simulation,

    /// <summary>Every trial question was tried: the rest of the exam needs a purchase or subscription.</summary>
    Unlock,

    /// <summary>No exam goal yet: setting the date paces a daily plan (PLN-01).</summary>
    SetGoal,
}

public sealed record PlanStep(PlanStepKind Kind, long? TopicId = null, string? Topic = null, int Count = 0, int? Percent = null);

/// <summary>
/// Weak areas and the learning plan built from them (ANL-01, D-137). Pure: the caller loads the pool, the answers and
/// the facts. A topic's share counts each tried question once, by its latest answer, so practice that fixes a mistake
/// shows at once.
/// </summary>
public static class WeakAreas
{
    /// <summary>Questions a topic needs before it is rated.</summary>
    public const int MinTried = 3;

    /// <summary>Below this share right a topic is weak.</summary>
    public const int WeakBelowPercent = 60;

    /// <summary>From this share right a topic is strong.</summary>
    public const int StrongFromPercent = 80;

    /// <summary>Weak topics in a plan.</summary>
    public const int TopicSteps = 2;

    /// <summary>Questions in one topic step.</summary>
    public const int QuestionsPerStep = 20;

    /// <summary>Tried questions before a simulation is suggested.</summary>
    public const int SimulationAfterTried = 20;

    /// <summary>Days between suggested simulations.</summary>
    public const int SimulationEveryDays = 7;

    public static int Percent(int part, int whole) =>
        whole == 0 ? 0 : (int)Math.Round(part * 100m / whole, MidpointRounding.AwayFromZero);

    public static Strength StrengthOf(int tried, int rightNow)
    {
        if (tried < MinTried)
        {
            return Strength.NotEnough;
        }

        var percent = Percent(rightNow, tried);
        return percent < WeakBelowPercent ? Strength.Weak : percent < StrongFromPercent ? Strength.Fair : Strength.Strong;
    }

    /// <summary>
    /// Every topic of the pool, weakest first: rated topics by share right (lowest first, then most tried), then topics
    /// tried too little to rate, then untried ones; ties by name.
    /// </summary>
    public static IReadOnlyList<TopicStanding> Topics(IEnumerable<PoolQuestion> pool, IEnumerable<QuestionAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(pool);
        var latest = Latest(answers);
        return [.. pool
            .GroupBy(q => (q.TopicId, q.Topic))
            .Select(g => new TopicStanding(
                g.Key.TopicId,
                g.Key.Topic,
                g.Count(),
                g.Count(q => latest.ContainsKey(q.QuestionId)),
                g.Count(q => latest.TryGetValue(q.QuestionId, out var right) && right)))
            .OrderBy(t => t.Strength == Strength.NotEnough ? (t.Tried == 0 ? 2 : 1) : 0)
            .ThenBy(t => t.Strength == Strength.NotEnough ? 0 : t.Percent)
            .ThenByDescending(t => t.Tried)
            .ThenBy(t => t.Topic, StringComparer.CurrentCulture)];
    }

    /// <summary>The SKD subtests found in the topic names, in TWK, TIU, TKP order; empty when the exam has none.</summary>
    public static IReadOnlyList<SubtestStanding> Subtests(IEnumerable<TopicStanding> topics)
    {
        ArgumentNullException.ThrowIfNull(topics);
        var bySubtest = topics
            .Select(t => (Subtest: SkdScoring.SubtestOf(t.Topic), Topic: t))
            .Where(x => x.Subtest is not null)
            .GroupBy(x => x.Subtest!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Topic).ToList());
        return [.. SkdScoring.Rules
            .Where(r => bySubtest.ContainsKey(r.Subtest))
            .Select(r => new SubtestStanding(
                r.Subtest,
                r.Code,
                bySubtest[r.Subtest].Sum(t => t.Questions),
                bySubtest[r.Subtest].Sum(t => t.Tried),
                bySubtest[r.Subtest].Sum(t => t.RightNow)))];
    }

    /// <summary>
    /// The learning plan, in the order to do it: due flashcards first (spaced repetition keeps what was learned), then
    /// the weakest rated topics, topped up with untried topics, then a simulation when one is due (or, on a trial, the
    /// offer to unlock the rest once every trial question was tried), and finally setting an exam goal if there is none.
    /// </summary>
    /// <param name="topics">From <see cref="Topics"/>, weakest first.</param>
    /// <param name="dueCards">Review cards of this exam due now.</param>
    /// <param name="trialOnly">The learner has the trial set only.</param>
    /// <param name="hasGoal">An active exam goal exists for this exam.</param>
    /// <param name="lastSimulationAt">The last finished simulation of this exam, if any.</param>
    public static IReadOnlyList<PlanStep> Plan(
        IReadOnlyList<TopicStanding> topics, int dueCards, bool trialOnly, bool hasGoal, DateTime? lastSimulationAt, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(topics);
        var steps = new List<PlanStep>();
        if (dueCards > 0)
        {
            steps.Add(new PlanStep(PlanStepKind.ReviewCards, Count: dueCards));
        }

        var weak = topics
            .Where(t => t.Strength is Strength.Weak or Strength.Fair)
            .Take(TopicSteps)
            .Select(t => new PlanStep(PlanStepKind.PractiseTopic, t.TopicId, t.Topic, Math.Min(QuestionsPerStep, t.Questions), t.Percent))
            .ToList();
        steps.AddRange(weak);
        steps.AddRange(topics
            .Where(t => t.Tried < t.Questions && t.Strength is not (Strength.Weak or Strength.Fair))
            .OrderBy(t => t.Tried)
            .ThenBy(t => t.Topic, StringComparer.CurrentCulture)
            .Take(TopicSteps - weak.Count)
            .Select(t => new PlanStep(PlanStepKind.TryTopic, t.TopicId, t.Topic, Math.Min(QuestionsPerStep, t.Questions - t.Tried))));

        var tried = topics.Sum(t => t.Tried);
        var pool = topics.Sum(t => t.Questions);
        if (trialOnly)
        {
            if (pool > 0 && tried >= pool)
            {
                steps.Add(new PlanStep(PlanStepKind.Unlock));
            }
        }
        else if (tried >= Math.Min(SimulationAfterTried, pool) && tried > 0
            && (lastSimulationAt is null || now - lastSimulationAt.Value >= TimeSpan.FromDays(SimulationEveryDays)))
        {
            steps.Add(new PlanStep(PlanStepKind.Simulation));
        }

        if (!hasGoal)
        {
            steps.Add(new PlanStep(PlanStepKind.SetGoal));
        }

        return steps;
    }

    /// <summary>Each question's latest answer: right or wrong.</summary>
    private static Dictionary<long, bool> Latest(IEnumerable<QuestionAnswer> answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        return answers
            .GroupBy(a => a.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.At).ThenByDescending(a => a.Order).First().IsCorrect);
    }
}
