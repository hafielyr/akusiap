using Akusiap.Domain.Access;

namespace Akusiap.UnitTests.Domain.Access;

/// <summary>OWN-02: own-content allowance for each kind of user (D-11, D-07).</summary>
public sealed class AllowancePolicyTests
{
    private static readonly PlanCaps Free = new(50, 1);
    private static readonly PlanCaps Learner = new(5000, 100);

    [Fact]
    public void Free_user_gets_the_free_caps()
    {
        var allowance = Evaluate(usedQuestions: 49, usedExams: 0);

        Assert.Equal((50, 1), (allowance.MaxQuestions!.Value, allowance.MaxExams!.Value));
        Assert.True(allowance.CanCreateQuestion);
        Assert.True(allowance.CanCreateExam);
        Assert.Equal(1, allowance.QuestionsLeft);
    }

    [Fact]
    public void Free_user_at_the_cap_can_edit_but_not_create()
    {
        var allowance = Evaluate(usedQuestions: 50, usedExams: 1);

        Assert.True(allowance.CanEdit);
        Assert.False(allowance.CanCreateQuestion);
        Assert.False(allowance.CanCreateExam);
    }

    [Fact]
    public void Exam_buyer_gets_the_largest_exam_allowance_not_the_sum()
    {
        var allowance = Evaluate(examAllowances: [200, 120], usedQuestions: 150);

        Assert.Equal(200, allowance.MaxQuestions);
        Assert.Equal(1, allowance.MaxExams); // purchases only raise the question cap
        Assert.True(allowance.CanCreateQuestion);
    }

    [Fact]
    public void Subscriber_gets_the_plan_caps()
    {
        var allowance = Evaluate(plans: [Learner], examAllowances: [200], usedQuestions: 3000, usedExams: 20);

        Assert.Equal((5000, 100), (allowance.MaxQuestions!.Value, allowance.MaxExams!.Value));
        Assert.True(allowance.CanCreateQuestion && allowance.CanCreateExam);
    }

    [Fact]
    public void Expired_subscriber_over_the_free_cap_becomes_read_only()
    {
        var allowance = Evaluate(usedQuestions: 300, usedExams: 3);

        Assert.False(allowance.CanEdit);
        Assert.False(allowance.CanCreateQuestion);
        Assert.False(allowance.CanCreateExam);
        Assert.Equal(0, allowance.QuestionsLeft);
    }

    [Fact]
    public void Admin_is_unlimited()
    {
        var allowance = AllowancePolicy.Evaluate(new AllowanceInputs(true, Free, [], [], 99_999, 999));

        Assert.True(allowance.IsUnlimited);
        Assert.True(allowance.CanEdit && allowance.CanCreateQuestion && allowance.CanCreateExam);
        Assert.Null(allowance.QuestionsLeft);
    }

    [Fact]
    public void Unconfigured_caps_and_plans_without_authoring_count_as_zero()
    {
        var allowance = Evaluate(free: new PlanCaps(null, null), plans: [new PlanCaps(5000, 100, IncludesAuthoring: false), new PlanCaps(null, 10)]);

        Assert.Equal((0, 10), (allowance.MaxQuestions!.Value, allowance.MaxExams!.Value));
        Assert.False(allowance.CanCreateQuestion);
        Assert.True(allowance.CanCreateExam);
    }

    private static Allowance Evaluate(PlanCaps? free = null, PlanCaps[]? plans = null, int[]? examAllowances = null, int usedQuestions = 0, int usedExams = 0) =>
        AllowancePolicy.Evaluate(new AllowanceInputs(false, free ?? Free, plans ?? [], examAllowances ?? [], usedQuestions, usedExams));
}
