using Akusiap.Application.Catalog;
using Akusiap.Domain.Access;

namespace Akusiap.UnitTests.Application;

/// <summary>CAT-02: which buttons the exam page shows for each access decision.</summary>
public sealed class ExamCtaTests
{
    [Theory]
    [InlineData(AccessLevel.Full, AccessReason.Purchased)]
    [InlineData(AccessLevel.Full, AccessReason.Subscription)]
    [InlineData(AccessLevel.Full, AccessReason.FreeExam)]
    [InlineData(AccessLevel.Admin, AccessReason.Admin)]
    [InlineData(AccessLevel.Owner, AccessReason.Owner)]
    [InlineData(AccessLevel.Shared, AccessReason.SharedLink)]
    public void Full_access_starts_practice(AccessLevel level, AccessReason reason)
    {
        Assert.Equal([ExamCta.StartPractice], ExamCtas.For(Decision(level, reason)));
    }

    [Fact]
    public void Trial_offers_the_free_questions_then_buy_and_subscribe()
    {
        Assert.Equal(
            [ExamCta.StartTrial, ExamCta.Buy, ExamCta.Subscribe],
            ExamCtas.For(Decision(AccessLevel.Trial, AccessReason.TrialAvailable)));
    }

    [Fact]
    public void Guest_on_a_free_exam_is_asked_to_register()
    {
        Assert.Equal([ExamCta.RegisterToUnlock], ExamCtas.For(Decision(AccessLevel.None, AccessReason.RegisterToUnlock)));
    }

    [Fact]
    public void No_access_without_a_trial_offers_buy_and_subscribe()
    {
        Assert.Equal([ExamCta.Buy, ExamCta.Subscribe], ExamCtas.For(Decision(AccessLevel.None, AccessReason.NoAccess)));
    }

    [Fact]
    public void Owner_of_a_taken_down_exam_gets_no_button()
    {
        Assert.Empty(ExamCtas.For(Decision(AccessLevel.Owner, AccessReason.OwnerTakenDown)));
    }

    [Theory]
    [InlineData(AccessLevel.None, AccessReason.NotPublished, false)]
    [InlineData(AccessLevel.None, AccessReason.TakenDown, false)]
    [InlineData(AccessLevel.None, AccessReason.NoAccess, true)]
    [InlineData(AccessLevel.None, AccessReason.RegisterToUnlock, true)]
    [InlineData(AccessLevel.Trial, AccessReason.TrialAvailable, true)]
    [InlineData(AccessLevel.Admin, AccessReason.Admin, true)]
    public void Unpublished_and_taken_down_exams_are_hidden(AccessLevel level, AccessReason reason, bool visible)
    {
        Assert.Equal(visible, ExamCtas.IsVisible(Decision(level, reason)));
    }

    private static AccessDecision Decision(AccessLevel level, AccessReason reason) =>
        new(level, reason, level switch
        {
            AccessLevel.None => QuestionScope.None,
            AccessLevel.Trial => QuestionScope.TrialOnly,
            _ => QuestionScope.All,
        });
}
