using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;

namespace Akusiap.UnitTests.Domain;

/// <summary>SES-06 and D-02: scoring per question type and attempt totals.</summary>
public sealed class ScoringTests
{
    [Theory]
    [InlineData(new long[] { 2 }, true)]
    [InlineData(new long[] { 1 }, false)]
    [InlineData(new long[] { 1, 2 }, false)]
    [InlineData(new long[0], false)]
    public void Single_choice_needs_exactly_the_correct_option(long[] selected, bool correct)
    {
        Assert.Equal(correct, Scoring.IsCorrect(QuestionType.SingleChoice, [2], selected));
    }

    [Theory]
    [InlineData(new long[] { 1 }, true)]
    [InlineData(new long[] { 2 }, false)]
    [InlineData(new long[0], false)]
    public void True_false_works_like_single_choice(long[] selected, bool correct)
    {
        Assert.Equal(correct, Scoring.IsCorrect(QuestionType.TrueFalse, [1], selected));
    }

    [Theory]
    [InlineData(new long[] { 1, 3 }, true)]
    [InlineData(new long[] { 3, 1 }, true)]
    [InlineData(new long[] { 1 }, false)]
    [InlineData(new long[] { 1, 2, 3 }, false)]
    [InlineData(new long[0], false)]
    public void Multi_select_is_all_or_nothing(long[] selected, bool correct)
    {
        Assert.Equal(correct, Scoring.IsCorrect(QuestionType.MultiSelect, [1, 3], selected));
    }

    [Fact]
    public void Totals_round_to_two_decimals_and_compare_with_the_pass_mark()
    {
        var score = Scoring.Total([true, true, false], passMarkPercent: 65);

        Assert.Equal(new AttemptScore(2, 3, 66.67m, true), score);
        Assert.False(Scoring.Total([true, false, false], 65).Passed);
        Assert.Null(Scoring.Total([true], null).Passed);
        Assert.Equal(new AttemptScore(0, 0, 0m, null), Scoring.Total([], null));
    }
}
