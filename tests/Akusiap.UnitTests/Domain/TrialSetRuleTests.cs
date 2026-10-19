using Akusiap.Domain.Content;

namespace Akusiap.UnitTests.Domain;

/// <summary>D-08: a paid exam needs 20–40 trial questions, or all of them when it has fewer than 30.</summary>
public sealed class TrialSetRuleTests
{
    [Theory]
    [InlineData(30, 20, 30)]
    [InlineData(35, 20, 35)]
    [InlineData(40, 20, 40)]
    [InlineData(120, 20, 40)]
    public void Exams_with_30_or_more_questions_need_20_to_40_trial_questions(int total, int min, int max)
    {
        Assert.Equal(new TrialRequirement(min, max), TrialSetRule.For(total));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(29)]
    public void Exams_with_fewer_than_30_questions_need_all_of_them_as_trial(int total)
    {
        Assert.Equal(new TrialRequirement(total, total), TrialSetRule.For(total));
    }

    [Theory]
    [InlineData(40, 19, false)]
    [InlineData(40, 20, true)]
    [InlineData(40, 30, true)]
    [InlineData(50, 40, true)]
    [InlineData(50, 41, false)]
    [InlineData(25, 24, false)]
    [InlineData(25, 25, true)]
    public void Is_met_only_inside_the_range(int total, int trial, bool met)
    {
        Assert.Equal(met, TrialSetRule.For(total).IsMetBy(trial));
    }

    [Fact]
    public void An_exam_without_questions_needs_none()
    {
        Assert.Equal(new TrialRequirement(0, 0), TrialSetRule.For(0));
    }

    [Fact]
    public void Negative_counts_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TrialSetRule.For(-1));
    }
}
