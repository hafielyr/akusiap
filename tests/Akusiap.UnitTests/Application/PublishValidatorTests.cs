using Akusiap.Application.Exams;
using Akusiap.Domain.Content;

namespace Akusiap.UnitTests.Application;

/// <summary>ADM-T6: what an exam needs before it can be published (D-08 for the trial set).</summary>
public sealed class PublishValidatorTests
{
    [Fact]
    public void A_free_exam_with_one_active_question_can_be_published()
    {
        Assert.Empty(PublishValidator.Check(Free() with { ActiveQuestionCount = 1, ActiveTrialCount = 0 }));
    }

    [Fact]
    public void A_ready_paid_exam_can_be_published()
    {
        Assert.Empty(PublishValidator.Check(Paid(active: 40, trial: 30)));
    }

    [Fact]
    public void Every_exam_needs_at_least_one_active_question()
    {
        var problem = Assert.Single(PublishValidator.Check(Free() with { ActiveQuestionCount = 0 }));

        Assert.Equal(PublishValidator.NoActiveQuestions, problem.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void A_paid_exam_needs_a_price(long? price)
    {
        var problem = Assert.Single(PublishValidator.Check(Paid(40, 30) with { PriceIdr = price }));

        Assert.Equal(PublishValidator.PriceMissing, problem.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void A_paid_exam_needs_access_days(int? days)
    {
        var problem = Assert.Single(PublishValidator.Check(Paid(40, 30) with { AccessDays = days }));

        Assert.Equal(PublishValidator.AccessDaysMissing, problem.Code);
    }

    [Theory]
    [InlineData(40, 19)]
    [InlineData(40, 41)]
    [InlineData(35, 36)]
    public void A_paid_exam_with_30_or_more_active_questions_needs_20_to_40_trial_questions(int active, int trial)
    {
        var problem = Assert.Single(PublishValidator.Check(Paid(active, trial)));

        Assert.Equal(PublishValidator.TrialRange, problem.Code);
        Assert.Equal(new object[] { active, 20, Math.Min(40, active), trial }, problem.Args);
    }

    [Theory]
    [InlineData(40, 20)]
    [InlineData(40, 40)]
    [InlineData(35, 35)]
    public void Trial_sets_inside_the_range_are_fine(int active, int trial)
    {
        Assert.Empty(PublishValidator.Check(Paid(active, trial)));
    }

    [Fact]
    public void A_small_paid_exam_needs_all_of_its_questions_as_trial()
    {
        Assert.Empty(PublishValidator.Check(Paid(active: 12, trial: 12)));

        var problem = Assert.Single(PublishValidator.Check(Paid(active: 12, trial: 11)));
        Assert.Equal(PublishValidator.TrialAll, problem.Code);
        Assert.Equal(new object[] { 12, 11 }, problem.Args);
    }

    [Fact]
    public void A_free_exam_has_no_trial_requirement()
    {
        Assert.Empty(PublishValidator.Check(Free() with { ActiveQuestionCount = 40, ActiveTrialCount = 3 }));
    }

    [Fact]
    public void Every_problem_is_listed_at_once()
    {
        var problems = PublishValidator.Check(Paid(active: 0, trial: 0) with { PriceIdr = null, AccessDays = null });

        Assert.Equal(
            new[] { PublishValidator.NoActiveQuestions, PublishValidator.PriceMissing, PublishValidator.AccessDaysMissing },
            problems.Select(p => p.Code).ToArray());
    }

    [Theory]
    [InlineData(ExamStatus.TakenDown, PublishValidator.TakenDown)]
    [InlineData(ExamStatus.Published, PublishValidator.AlreadyPublished)]
    public void Taken_down_or_already_published_exams_cannot_be_published(ExamStatus status, string code)
    {
        var problem = Assert.Single(PublishValidator.Check(Paid(40, 30) with { Status = status }));

        Assert.Equal(code, problem.Code);
    }

    [Theory]
    [InlineData(ExamStatus.Draft)]
    [InlineData(ExamStatus.Unpublished)]
    public void Drafts_and_unpublished_exams_can_be_published(ExamStatus status)
    {
        Assert.Empty(PublishValidator.Check(Paid(40, 30) with { Status = status }));
    }

    private static PublishFacts Free() => new(ExamStatus.Draft, IsFree: true, PriceIdr: null, AccessDays: null, ActiveQuestionCount: 10, ActiveTrialCount: 0);

    private static PublishFacts Paid(int active, int trial) =>
        new(ExamStatus.Draft, IsFree: false, PriceIdr: 49_000, AccessDays: 30, ActiveQuestionCount: active, ActiveTrialCount: trial);
}
