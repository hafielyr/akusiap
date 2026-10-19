using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;

namespace Akusiap.UnitTests.Domain;

/// <summary>SES-01: which questions an attempt gets and in what order, with a seeded random source.</summary>
public sealed class QuestionSelectionTests
{
    private static readonly IReadOnlyList<CandidateQuestion> Candidates =
    [
        .. Enumerable.Range(1, 10).Select(i => new CandidateQuestion(
            i,
            TopicId: i <= 6 ? 100 : 200,
            i == 10 ? QuestionType.TrueFalse : QuestionType.SingleChoice,
            i == 10 ? [1001, 1002] : [i * 10 + 1, i * 10 + 2, i * 10 + 3, i * 10 + 4])),
    ];

    [Fact]
    public void Without_shuffling_takes_the_first_questions_in_exam_order_with_options_as_saved()
    {
        var selected = QuestionSelection.Select(Candidates, [], count: 3, shuffleQuestions: false, shuffleOptions: false, new Random(1));

        Assert.Equal([1L, 2, 3], selected.Select(s => s.QuestionId));
        Assert.Equal([1, 2, 3], selected.Select(s => s.Position));
        Assert.Equal([11L, 12, 13, 14], selected[0].OptionOrder);
    }

    [Fact]
    public void Shuffling_is_repeatable_with_the_same_seed_and_differs_between_seeds()
    {
        var first = Ids(QuestionSelection.Select(Candidates, [], null, true, true, new Random(7)));
        var again = Ids(QuestionSelection.Select(Candidates, [], null, true, true, new Random(7)));
        var other = Ids(QuestionSelection.Select(Candidates, [], null, true, true, new Random(8)));

        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.Equal(Candidates.Select(c => c.QuestionId).Order(), first.Order());
    }

    [Fact]
    public void Shuffled_subset_is_a_random_pick_of_the_requested_size_with_positions_1_to_n()
    {
        var selected = QuestionSelection.Select(Candidates, [], count: 4, shuffleQuestions: true, shuffleOptions: false, new Random(3));

        Assert.Equal(4, selected.Count);
        Assert.Equal(4, selected.Select(s => s.QuestionId).Distinct().Count());
        Assert.Equal([1, 2, 3, 4], selected.Select(s => s.Position));
        Assert.NotEqual([1L, 2, 3, 4], selected.Select(s => s.QuestionId));
    }

    [Fact]
    public void Option_shuffle_keeps_every_option_and_leaves_true_false_alone()
    {
        var selected = QuestionSelection.Select(Candidates, [], null, shuffleQuestions: false, shuffleOptions: true, new Random(5));

        Assert.All(selected.Take(9), s => Assert.Equal(Candidates[(int)s.QuestionId - 1].OptionIds.Order(), s.OptionOrder.Order()));
        Assert.Contains(selected.Take(9), s => !s.OptionOrder.SequenceEqual(Candidates[(int)s.QuestionId - 1].OptionIds));
        Assert.Equal([1001L, 1002], selected[9].OptionOrder);
    }

    [Fact]
    public void Topic_filter_keeps_only_chosen_topics()
    {
        var selected = QuestionSelection.Select(Candidates, [200], null, shuffleQuestions: false, shuffleOptions: false, new Random(1));

        Assert.Equal([7L, 8, 9, 10], selected.Select(s => s.QuestionId));
        Assert.Equal(4, QuestionSelection.CountAvailable(Candidates, [200]));
        Assert.Equal(10, QuestionSelection.CountAvailable(Candidates, []));
    }

    [Fact]
    public void Count_larger_than_the_pool_takes_everything()
    {
        Assert.Equal(10, QuestionSelection.Select(Candidates, [], 50, false, false, new Random(1)).Count);
    }

    private static List<long> Ids(IReadOnlyList<SelectedQuestion> selected) => [.. selected.Select(s => s.QuestionId)];
}
