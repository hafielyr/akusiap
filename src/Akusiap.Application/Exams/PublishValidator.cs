using Akusiap.Application.Common;
using Akusiap.Domain.Content;

namespace Akusiap.Application.Exams;

/// <summary>
/// What publishing looks at. Only Active questions count: draft or retired questions in the exam are never used in new
/// attempts, so they can't make an exam publishable (ADM-T6).
/// </summary>
public sealed record PublishFacts(
    ExamStatus Status,
    bool IsFree,
    long? PriceIdr,
    int? AccessDays,
    int ActiveQuestionCount,
    int ActiveTrialCount);

/// <summary>Publish rules (ADM-T6, D-08). Pure; lists every problem at once so the admin can fix them in one go.</summary>
public static class PublishValidator
{
    public const string NoActiveQuestions = "Admin.Publish.NoActiveQuestions";
    public const string PriceMissing = "Admin.Publish.PriceMissing";
    public const string AccessDaysMissing = "Admin.Publish.AccessDaysMissing";

    /// <summary>Args: active questions, minimum, maximum, current trial count.</summary>
    public const string TrialRange = "Admin.Publish.TrialRange";

    /// <summary>Args: active questions, current trial count.</summary>
    public const string TrialAll = "Admin.Publish.TrialAll";
    public const string TakenDown = "Admin.Publish.TakenDown";
    public const string AlreadyPublished = "Admin.Publish.AlreadyPublished";

    public static List<Problem> Check(PublishFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (facts.Status == ExamStatus.TakenDown)
        {
            return [new Problem(TakenDown)];
        }

        if (facts.Status == ExamStatus.Published)
        {
            return [new Problem(AlreadyPublished)];
        }

        var problems = new List<Problem>();
        if (facts.ActiveQuestionCount < 1)
        {
            problems.Add(new Problem(NoActiveQuestions));
        }

        if (facts.IsFree)
        {
            return problems;
        }

        if (facts.PriceIdr is not > 0)
        {
            problems.Add(new Problem(PriceMissing));
        }

        if (facts.AccessDays is not > 0)
        {
            problems.Add(new Problem(AccessDaysMissing));
        }

        var requirement = TrialSetRule.For(facts.ActiveQuestionCount);
        if (facts.ActiveQuestionCount > 0 && !requirement.IsMetBy(facts.ActiveTrialCount))
        {
            problems.Add(requirement.Min == requirement.Max
                ? new Problem(TrialAll, null, facts.ActiveQuestionCount, facts.ActiveTrialCount)
                : new Problem(TrialRange, null, facts.ActiveQuestionCount, requirement.Min, requirement.Max, facts.ActiveTrialCount));
        }

        return problems;
    }
}
