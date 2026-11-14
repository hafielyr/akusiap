using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;

namespace Akusiap.UnitTests.Domain;

/// <summary>DIF-01 (D-140): the measured difficulty from right, wrong and marked counts.</summary>
public sealed class QuestionDifficultyTests
{
    [Theory]
    [InlineData(10, 0, 0, MeasuredDifficulty.Easy)]
    [InlineData(8, 2, 0, MeasuredDifficulty.Easy)] // 0.20
    [InlineData(7, 3, 0, MeasuredDifficulty.Medium)] // 0.30
    [InlineData(8, 2, 2, MeasuredDifficulty.Medium)] // (2 + 1) / 10 = 0.30: doubt makes it harder
    [InlineData(5, 5, 0, MeasuredDifficulty.Medium)] // 0.50
    [InlineData(4, 6, 0, MeasuredDifficulty.Hard)] // 0.60
    [InlineData(6, 4, 4, MeasuredDifficulty.Hard)] // (4 + 2) / 10 = 0.60
    [InlineData(0, 10, 10, MeasuredDifficulty.Hard)] // capped at 1
    public void The_score_sets_the_level(int correct, int wrong, int marked, MeasuredDifficulty expected) =>
        Assert.Equal(expected, QuestionDifficulty.Of(correct, wrong, marked));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 4, 9)]
    [InlineData(0, 9, 0)]
    public void Fewer_than_10_answers_get_no_label(int correct, int wrong, int marked) =>
        Assert.Null(QuestionDifficulty.Of(correct, wrong, marked));

    [Fact]
    public void The_question_label_carries_the_difficulty_only_with_stats()
    {
        Assert.Equal(new QuestionLabel("TWK", "Pancasila", MeasuredDifficulty.Hard), QuestionLabel.From("TWK · Pancasila", 2, 8, 0));
        Assert.Null(QuestionLabel.From("TWK · Pancasila", null, null, null).Difficulty);
        Assert.False(QuestionLabel.From(null, 10, 0, 0).IsEmpty);
        Assert.True(QuestionLabel.From(null, null, null, null).IsEmpty);
    }
}
