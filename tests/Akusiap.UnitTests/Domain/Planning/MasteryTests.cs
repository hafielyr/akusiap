using Akusiap.Domain.Memorization;
using Akusiap.Domain.Planning;

namespace Akusiap.UnitTests.Domain.Planning;

/// <summary>PLN-04 (D-122): when a question is mastered, topic shares, weakest first, and the schedule check.</summary>
public sealed class MasteryTests
{
    private static readonly DateOnly Start = new(2026, 11, 7);

    [Theory]
    [InlineData(null, null, false)] // never answered
    [InlineData(true, null, true)] // right, no card
    [InlineData(false, null, false)] // wrong (a card is made at once in practice)
    [InlineData(false, ReviewCardState.Learning, false)] // answered wrong, not reviewed yet
    [InlineData(true, ReviewCardState.Learning, false)] // card still being learned
    [InlineData(true, ReviewCardState.Review, true)]
    [InlineData(false, ReviewCardState.Memorized, true)] // the card decides once there is one
    public void A_question_is_mastered_by_its_last_answer_or_its_card(bool? lastCorrect, ReviewCardState? card, bool expected)
    {
        Assert.Equal(expected, Mastery.IsMastered(lastCorrect, card));
    }

    [Fact]
    public void Topics_give_whole_percentages_and_the_weakest_comes_first()
    {
        var topics = Mastery.WeakestFirst(
        [
            new TopicProgress(1, "TWK", Total: 4, Answered: 4, Mastered: 3),
            new TopicProgress(2, "TIU", Total: 3, Answered: 2, Mastered: 0),
            new TopicProgress(3, "TKP", Total: 3, Answered: 1, Mastered: 0),
        ]);

        Assert.Equal(["TKP", "TIU", "TWK"], topics.Select(t => t.Name));
        Assert.Equal((67, 0), (topics[1].AnsweredPercent, topics[1].MasteredPercent));
        Assert.Equal((100, 75), (topics[2].AnsweredPercent, topics[2].MasteredPercent));
        Assert.Equal(0, new TopicProgress(9, "Kosong", 0, 0, 0).AnsweredPercent);
    }

    [Theory]
    [InlineData(0, 0)] // the day the goal is set
    [InlineData(10, 25)] // 10 of 40 learning days: a quarter of 100
    [InlineData(39, 98)] // ceil(100 × 39 / 40)
    [InlineData(40, 100)] // the review phase: everything
    [InlineData(47, 100)]
    public void Expected_answers_follow_a_straight_line_over_the_learning_days(int day, int expected)
    {
        // 47 days: 7 review days, 40 learning days.
        Assert.Equal(expected, StudyPlanner.ExpectedAnswered(Start, Start.AddDays(47), Start.AddDays(day), poolSize: 100));
    }
}
