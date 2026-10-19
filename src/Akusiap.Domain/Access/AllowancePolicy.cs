namespace Akusiap.Domain.Access;

/// <summary>A plan's authoring caps. Null means "not configured" and counts as 0 (blocked, never unlimited).</summary>
public sealed record PlanCaps(int? MaxOwnQuestions, int? MaxOwnExams, bool IncludesAuthoring = true);

/// <summary>Everything the allowance depends on for one user.</summary>
/// <param name="FreePlan">The Free plan, which every registered user has.</param>
/// <param name="ActivePlans">Plans of the user's subscriptions that are Active and not yet ended.</param>
/// <param name="ActiveExamAllowances">OwnQuestionAllowance of each exam the user holds an active purchase for.</param>
public sealed record AllowanceInputs(
    bool IsAdmin,
    PlanCaps FreePlan,
    IReadOnlyList<PlanCaps> ActivePlans,
    IReadOnlyList<int> ActiveExamAllowances,
    int UsedQuestions,
    int UsedExams);

/// <summary>The user's own-content allowance. Max values are null only for admins (unlimited).</summary>
public sealed record Allowance(int? MaxQuestions, int? MaxExams, int UsedQuestions, int UsedExams)
{
    public bool IsUnlimited => MaxQuestions is null && MaxExams is null;

    /// <summary>
    /// D-07: own content is read-only (view and practise only) when no entitlement covers what the user already has,
    /// for example after a subscription ends with usage above the Free caps.
    /// </summary>
    public bool CanEdit => IsUnlimited || (UsedQuestions <= MaxQuestions && UsedExams <= MaxExams);

    public bool CanCreateQuestion => CanEdit && (MaxQuestions is null || UsedQuestions < MaxQuestions);

    public bool CanCreateExam => CanEdit && (MaxExams is null || UsedExams < MaxExams);

    /// <summary>Questions still allowed; null when unlimited.</summary>
    public int? QuestionsLeft => MaxQuestions is { } max ? Math.Max(0, max - UsedQuestions) : null;

    public int? ExamsLeft => MaxExams is { } max ? Math.Max(0, max - UsedExams) : null;
}

/// <summary>
/// The own-question and own-exam allowance (OWN-02, stories C-03, C-04, C-12; D-11, D-07, D-66). Caps are the
/// maximum, not the sum, of: the Free plan, every active subscription plan that includes authoring, and, for questions
/// only, every actively purchased exam's <c>OwnQuestionAllowance</c>. Unconfigured caps count as 0. Admins are unlimited.
/// </summary>
public static class AllowancePolicy
{
    public static Allowance Evaluate(AllowanceInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.IsAdmin)
        {
            return new Allowance(null, null, inputs.UsedQuestions, inputs.UsedExams);
        }

        var plans = inputs.ActivePlans.Where(p => p.IncludesAuthoring).Prepend(inputs.FreePlan).ToList();
        var maxQuestions = plans.Select(p => p.MaxOwnQuestions ?? 0).Concat(inputs.ActiveExamAllowances).Max();
        var maxExams = plans.Max(p => p.MaxOwnExams ?? 0);
        return new Allowance(Math.Max(0, maxQuestions), Math.Max(0, maxExams), inputs.UsedQuestions, inputs.UsedExams);
    }
}
