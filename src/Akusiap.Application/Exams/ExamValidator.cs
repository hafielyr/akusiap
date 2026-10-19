using Akusiap.Application.Common;

namespace Akusiap.Application.Exams;

/// <summary>Create (Id null) or edit an app exam (ADM-06, ADM-08, ADM-09). A free exam ignores price and access days.</summary>
public sealed record SaveExamRequest(
    long? Id,
    string? Title,
    string? Description,
    long? SubjectId,
    bool IsFree,
    long? PriceIdr,
    int? AccessDays,
    int OwnQuestionAllowance,
    long RulePresetId);

/// <summary>The rules an app exam must meet before it is saved (ADM-T3). Pure; returns every problem at once.</summary>
public static class ExamValidator
{
    public const string TitleField = "Title";
    public const string DescriptionField = "Description";
    public const string SubjectField = "SubjectId";
    public const string PriceField = "PriceIdr";
    public const string AccessDaysField = "AccessDays";
    public const string AllowanceField = "OwnQuestionAllowance";
    public const string RulePresetField = "RulePresetId";

    public const int DescriptionMaxLength = 4000;
    public const long MaxPriceIdr = 100_000_000;
    public const int MaxAccessDays = 3650;
    public const int MaxAllowance = 10_000;

    public const string TitleLabel = "Admin.Exam.Title";
    public const string DescriptionLabel = "Admin.Exam.Description";

    public const string PriceRequired = "Admin.Exams.PriceRequired";
    public const string PriceTooHigh = "Admin.Exams.PriceTooHigh";
    public const string PriceInvalid = "Admin.Exams.PriceInvalid";
    public const string AccessDaysRequired = "Admin.Exams.AccessDaysRequired";
    public const string AccessDaysTooHigh = "Admin.Exams.AccessDaysTooHigh";

    /// <summary>Arg: maximum.</summary>
    public const string AllowanceRange = "Admin.Exams.AllowanceRange";
    public const string RulePresetRequired = "Admin.Exams.RulePresetRequired";
    public const string SubjectMissing = "Admin.Exams.SubjectMissing";

    public static List<Problem> Validate(SaveExamRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new List<Problem>();

        TextRules.RequireName(problems, TitleField, TitleLabel, request.Title);
        if (TextRules.Clean(request.Description) is { Length: > DescriptionMaxLength })
        {
            problems.Add(new Problem(TextRules.TooLong, DescriptionField, DescriptionLabel, DescriptionMaxLength));
        }

        if (!request.IsFree)
        {
            if (request.PriceIdr is not > 0)
            {
                problems.Add(new Problem(PriceRequired, PriceField));
            }
            else if (request.PriceIdr > MaxPriceIdr)
            {
                problems.Add(new Problem(PriceTooHigh, PriceField));
            }

            if (request.AccessDays is not > 0)
            {
                problems.Add(new Problem(AccessDaysRequired, AccessDaysField));
            }
            else if (request.AccessDays > MaxAccessDays)
            {
                problems.Add(new Problem(AccessDaysTooHigh, AccessDaysField));
            }
        }

        if (request.OwnQuestionAllowance is < 0 or > MaxAllowance)
        {
            problems.Add(new Problem(AllowanceRange, AllowanceField, MaxAllowance));
        }

        if (request.RulePresetId <= 0)
        {
            problems.Add(new Problem(RulePresetRequired, RulePresetField));
        }

        return problems;
    }
}
