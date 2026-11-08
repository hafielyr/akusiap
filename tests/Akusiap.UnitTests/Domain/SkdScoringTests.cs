using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;

namespace Akusiap.UnitTests.Domain;

/// <summary>CPNS-03 and CPNS-04: SKD subtests, scores, passing grades and the tryout composition.</summary>
public sealed class SkdScoringTests
{
    [Theory]
    [InlineData("Tes Wawasan Kebangsaan (TWK)", SkdSubtest.Twk)]
    [InlineData("TIU · Numerik", SkdSubtest.Tiu)]
    [InlineData("tkp", SkdSubtest.Tkp)]
    public void A_topic_name_with_one_code_maps_to_its_subtest(string name, SkdSubtest expected) =>
        Assert.Equal(expected, SkdScoring.SubtestOf(name));

    [Theory]
    [InlineData("Matematika")]
    [InlineData("TWKS")]
    [InlineData("TWK dan TIU")]
    [InlineData("")]
    public void Other_topic_names_map_to_no_subtest(string name) => Assert.Null(SkdScoring.SubtestOf(name));

    [Fact]
    public void The_rules_are_the_2024_ones()
    {
        Assert.Equal(110, SkdScoring.QuestionCount);
        Assert.Equal(54, SkdScoring.SecondsPerQuestion);
        Assert.Equal((30, 65, 150), (SkdScoring.Rule(SkdSubtest.Twk).QuestionCount, SkdScoring.Rule(SkdSubtest.Twk).PassingGrade, SkdScoring.Rule(SkdSubtest.Twk).MaxScore));
        Assert.Equal((35, 80), (SkdScoring.Rule(SkdSubtest.Tiu).QuestionCount, SkdScoring.Rule(SkdSubtest.Tiu).PassingGrade));
        Assert.Equal((45, 166), (SkdScoring.Rule(SkdSubtest.Tkp).QuestionCount, SkdScoring.Rule(SkdSubtest.Tkp).PassingGrade));
    }

    [Fact]
    public void Twk_and_tiu_give_five_for_right_and_zero_otherwise()
    {
        Assert.Equal(5, SkdScoring.ItemScore(new SkdItem(SkdSubtest.Twk, true, null, false)));
        Assert.Equal(0, SkdScoring.ItemScore(new SkdItem(SkdSubtest.Tiu, false, null, false)));
    }

    [Fact]
    public void Tkp_scores_the_picked_option_and_blank_is_zero()
    {
        Assert.Equal(3, SkdScoring.ItemScore(new SkdItem(SkdSubtest.Tkp, false, 3, true)));
        Assert.Equal(0, SkdScoring.ItemScore(new SkdItem(SkdSubtest.Tkp, false, null, true)));
        Assert.Equal(5, SkdScoring.ItemScore(new SkdItem(SkdSubtest.Tkp, true, null, false)));
    }

    [Fact]
    public void A_full_test_passes_only_when_every_subtest_reaches_its_grade()
    {
        var passing = Items(SkdSubtest.Twk, 30, right: 13).Concat(Items(SkdSubtest.Tiu, 35, right: 16)).Concat(Tkp(45, points: 4)).ToList();
        var score = SkdScoring.Score(passing);
        Assert.True(score.IsFullTest);
        Assert.Equal(65 + 80 + 180, score.Total);
        Assert.Equal(550, score.MaxTotal);
        Assert.True(score.Passed);

        var twkShort = Items(SkdSubtest.Twk, 30, right: 12).Concat(Items(SkdSubtest.Tiu, 35, right: 35)).Concat(Tkp(45, points: 5)).ToList();
        Assert.False(SkdScoring.Score(twkShort).Passed);
    }

    [Fact]
    public void A_short_set_is_not_judged_but_projected_to_a_full_subtest()
    {
        var score = SkdScoring.Score(Items(SkdSubtest.Tiu, 10, right: 4));
        var tiu = score.Subtests.Single(s => s.Rule.Subtest == SkdSubtest.Tiu);

        Assert.False(score.IsFullTest);
        Assert.Null(score.Passed);
        Assert.Equal(20, tiu.Score);
        Assert.Equal(70, tiu.Projected); // 20 points in 10 questions → 70 in 35
        Assert.Equal(10, tiu.Gap);
        Assert.False(tiu.ReachesPassingGrade);
    }

    [Theory]
    [InlineData(new[] { 1, 2, 5, 4, 3 }, 2, true)]
    [InlineData(new[] { 1, 2, 5, 4, 3 }, 3, false)] // right option is not the best
    [InlineData(new[] { 1, 2, 6, 4, 3 }, 2, false)] // out of range
    public void Points_must_favour_the_right_option(int[] points, int correct, bool ok)
    {
        var options = points.Select((p, i) => (i == correct, (int?)p)).ToList();
        Assert.Equal(ok, SkdScoring.ArePointsConsistent(options));
    }

    [Fact]
    public void Some_points_missing_is_inconsistent_and_none_is_fine()
    {
        Assert.False(SkdScoring.ArePointsConsistent([(true, 5), (false, null)]));
        Assert.True(SkdScoring.ArePointsConsistent([(true, null), (false, null)]));
    }

    [Fact]
    public void A_tryout_takes_each_subtest_in_order_and_prefers_scored_tkp()
    {
        var candidates = new List<CandidateQuestion>();
        long id = 0;
        foreach (var (topic, count) in new[] { (1L, 40), (2L, 40), (3L, 60), (4L, 10) })
        {
            for (var i = 0; i < count; i++)
            {
                candidates.Add(new CandidateQuestion(++id, topic, QuestionType.SingleChoice, [1, 2]));
            }
        }

        var subtests = new Dictionary<long, SkdSubtest?> { [1] = SkdSubtest.Twk, [2] = SkdSubtest.Tiu, [3] = SkdSubtest.Tkp, [4] = null };
        var scored = candidates.Where(c => c.TopicId == 3).Take(50).Select(c => c.QuestionId).ToHashSet();

        var tryout = SkdScoring.Compose(candidates, subtests, scored, new Random(7));

        Assert.Equal(110, tryout.Count);
        Assert.All(tryout.Take(30), c => Assert.Equal(1, c.TopicId));
        Assert.All(tryout.Skip(30).Take(35), c => Assert.Equal(2, c.TopicId));
        Assert.All(tryout.Skip(65), c => Assert.Contains(c.QuestionId, scored));
        Assert.Equal(110, tryout.Select(c => c.QuestionId).Distinct().Count());
    }

    private static IEnumerable<SkdItem> Items(SkdSubtest subtest, int count, int right) =>
        Enumerable.Range(0, count).Select(i => new SkdItem(subtest, i < right, null, false));

    private static IEnumerable<SkdItem> Tkp(int count, int points) =>
        Enumerable.Range(0, count).Select(_ => new SkdItem(SkdSubtest.Tkp, points == 5, points, true));
}
