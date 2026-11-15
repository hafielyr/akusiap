using Akusiap.Domain.Planning;
using Akusiap.Domain.Sessions;

namespace Akusiap.UnitTests.Domain.Planning;

/// <summary>ANL-01 (D-137): topic standings from the latest answers, SKD subtests, and the learning plan.</summary>
public sealed class WeakAreasTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, 0, Strength.NotEnough)]
    [InlineData(2, 0, Strength.NotEnough)]
    [InlineData(5, 2, Strength.Weak)] // 40%
    [InlineData(5, 3, Strength.Fair)] // 60% is not weak
    [InlineData(10, 7, Strength.Fair)] // 70%
    [InlineData(5, 4, Strength.Strong)] // 80% is strong
    public void Strength_needs_three_tried_and_splits_at_60_and_80_percent(int tried, int right, Strength expected)
    {
        Assert.Equal(expected, WeakAreas.StrengthOf(tried, right));
    }

    [Fact]
    public void The_latest_answer_to_a_question_counts_so_fixing_a_mistake_shows_at_once()
    {
        var pool = Pool(("Numerik", 3));
        var answers = new[]
        {
            new QuestionAnswer(1, false, Now.AddDays(-2)),
            new QuestionAnswer(1, true, Now.AddDays(-1)),
            new QuestionAnswer(2, true, Now.AddDays(-1)),
            new QuestionAnswer(3, true, Now.AddDays(-3)),
            new QuestionAnswer(3, false, Now.AddDays(-3), Order: 2), // same time: the later item wins
        };

        var topic = Assert.Single(WeakAreas.Topics(pool, answers));

        Assert.Equal((3, 3, 2, 67, Strength.Fair), (topic.Questions, topic.Tried, topic.RightNow, topic.Percent, topic.Strength));
    }

    [Fact]
    public void Topics_come_weakest_first_then_too_early_to_rate_then_untried()
    {
        var pool = Pool(("Untried", 4), ("Strong", 4), ("Weak", 4), ("Early", 4), ("Fair", 4));
        var answers = Answers(pool, ("Strong", 4, 4), ("Weak", 4, 1), ("Early", 2, 0), ("Fair", 3, 2));

        var names = WeakAreas.Topics(pool, answers).Select(t => t.Topic);

        Assert.Equal(["Weak", "Fair", "Strong", "Early", "Untried"], names);
    }

    [Fact]
    public void Skd_subtests_add_up_their_topics_in_twk_tiu_tkp_order()
    {
        var pool = Pool(("TKP · Pelayanan publik", 4), ("TWK · Pancasila", 3), ("TWK · UUD 1945", 2), ("Bahasa Inggris", 2));
        var answers = Answers(pool, ("TKP · Pelayanan publik", 4, 4), ("TWK · Pancasila", 3, 1), ("TWK · UUD 1945", 2, 1));

        var subtests = WeakAreas.Subtests(WeakAreas.Topics(pool, answers));

        Assert.Equal([("TWK", 5, 5, 2, Strength.Weak), ("TKP", 4, 4, 4, Strength.Strong)], subtests.Select(s => (s.Code, s.Questions, s.Tried, s.RightNow, s.Strength)));
        Assert.Empty(WeakAreas.Subtests(WeakAreas.Topics(Pool(("Anatomi", 3)), [])));
    }

    [Fact]
    public void Plan_reviews_due_cards_then_practises_the_two_weakest_topics_then_simulates_and_asks_for_a_goal()
    {
        var pool = Pool(("A", 30), ("B", 10), ("C", 10), ("D", 10));
        var topics = WeakAreas.Topics(pool, Answers(pool, ("A", 10, 3), ("B", 10, 7), ("C", 10, 5), ("D", 10, 10)));

        var plan = WeakAreas.Plan(topics, dueCards: 6, trialOnly: false, hasGoal: false, lastSimulationAt: null, Now);

        Assert.Equal(
            [
                new PlanStep(PlanStepKind.ReviewCards, Count: 6),
                new PlanStep(PlanStepKind.PractiseTopic, topics[0].TopicId, "A", 20, 30),
                new PlanStep(PlanStepKind.PractiseTopic, topics[1].TopicId, "C", 10, 50),
                new PlanStep(PlanStepKind.Simulation),
                new PlanStep(PlanStepKind.SetGoal),
            ],
            plan);
    }

    [Fact]
    public void Without_weak_topics_the_plan_tries_untouched_topics_with_the_questions_left()
    {
        var pool = Pool(("Done", 5), ("Half", 10), ("New", 30));
        var topics = WeakAreas.Topics(pool, Answers(pool, ("Done", 5, 5), ("Half", 2, 2)));

        var plan = WeakAreas.Plan(topics, dueCards: 0, trialOnly: false, hasGoal: true, lastSimulationAt: null, Now);

        Assert.Equal(
            [
                new PlanStep(PlanStepKind.TryTopic, topics.Single(t => t.Topic == "New").TopicId, "New", 20),
                new PlanStep(PlanStepKind.TryTopic, topics.Single(t => t.Topic == "Half").TopicId, "Half", 8),
            ],
            plan); // 7 tried is too few for a simulation
    }

    [Fact]
    public void A_recent_simulation_is_not_suggested_again_within_seven_days()
    {
        var pool = Pool(("A", 25));
        var topics = WeakAreas.Topics(pool, Answers(pool, ("A", 25, 25)));

        Assert.DoesNotContain(WeakAreas.Plan(topics, 0, false, true, Now.AddDays(-6), Now), s => s.Kind == PlanStepKind.Simulation);
        Assert.Contains(WeakAreas.Plan(topics, 0, false, true, Now.AddDays(-7), Now), s => s.Kind == PlanStepKind.Simulation);
    }

    [Fact]
    public void On_a_trial_the_plan_offers_to_unlock_once_every_trial_question_was_tried_and_never_a_simulation()
    {
        var pool = Pool(("TWK", 10), ("TIU", 10), ("TKP", 10));
        var some = WeakAreas.Topics(pool, Answers(pool, ("TWK", 10, 4), ("TIU", 10, 9)));
        var all = WeakAreas.Topics(pool, Answers(pool, ("TWK", 10, 4), ("TIU", 10, 9), ("TKP", 10, 10)));

        Assert.DoesNotContain(WeakAreas.Plan(some, 0, true, true, null, Now), s => s.Kind is PlanStepKind.Unlock or PlanStepKind.Simulation);
        Assert.Equal([PlanStepKind.PractiseTopic, PlanStepKind.Unlock], WeakAreas.Plan(all, 0, true, true, null, Now).Select(s => s.Kind));
    }

    private static List<PoolQuestion> Pool(params (string Topic, int Count)[] topics)
    {
        var pool = new List<PoolQuestion>();
        var id = 0L;
        for (var t = 0; t < topics.Length; t++)
        {
            for (var i = 0; i < topics[t].Count; i++)
            {
                pool.Add(new PoolQuestion(++id, t + 1, topics[t].Topic));
            }
        }

        return pool;
    }

    /// <summary>For each topic, answers its first <c>Tried</c> questions, the first <c>Right</c> of them right.</summary>
    private static List<QuestionAnswer> Answers(List<PoolQuestion> pool, params (string Topic, int Tried, int Right)[] topics) =>
        [.. topics.SelectMany(t => pool.Where(q => q.Topic == t.Topic).Take(t.Tried)
            .Select((q, i) => new QuestionAnswer(q.QuestionId, i < t.Right, Now.AddHours(-1))))];
}
