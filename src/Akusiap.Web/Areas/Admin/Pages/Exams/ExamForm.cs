using Akusiap.Application.Common;
using Akusiap.Application.Exams;
using Akusiap.Application.RulePresets;
using Akusiap.Application.Subjects;
using Akusiap.Domain;

namespace Akusiap.Web.Areas.Admin.Pages.Exams;

/// <summary>Exam form fields, posted as <c>Input.*</c>. Rules live in <see cref="ExamValidator"/>.</summary>
public sealed class ExamInput
{
    /// <summary>Suggested access period on a blank form.</summary>
    public const int DefaultAccessDays = 30;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public long? SubjectId { get; set; }

    public bool IsFree { get; set; }

    /// <summary>As typed: "49000", "49.000" or "Rp49.000".</summary>
    public string? PriceIdr { get; set; }

    /// <summary>No default here: an unreadable value ("abc") must arrive as missing, not as a silent 30.</summary>
    public int? AccessDays { get; set; }

    public int OwnQuestionAllowance { get; set; }

    public long RulePresetId { get; set; }

    public static ExamInput Blank(IReadOnlyList<RulePresetOption> presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        var preset = presets.FirstOrDefault(p => p.IsDefault) ?? (presets.Count > 0 ? presets[0] : null);
        return new ExamInput { RulePresetId = preset?.Id ?? 0, AccessDays = DefaultAccessDays };
    }

    public static ExamInput From(ExamDetail exam)
    {
        ArgumentNullException.ThrowIfNull(exam);
        return new ExamInput
        {
            Title = exam.Title,
            Description = exam.Description,
            SubjectId = exam.SubjectId,
            IsFree = exam.IsFree,
            PriceIdr = exam.PriceIdr is { } price ? Money.FormatIdr(price) : null,
            AccessDays = exam.AccessDays ?? DefaultAccessDays,
            OwnQuestionAllowance = exam.OwnQuestionAllowance,
            RulePresetId = exam.RulePresetId,
        };
    }

    /// <summary>The use-case request, or a problem when the typed price is not a whole Rupiah amount.</summary>
    public (SaveExamRequest Request, Problem? PriceProblem) ToRequest(long? id)
    {
        long? price = null;
        Problem? priceProblem = null;
        if (!IsFree && !string.IsNullOrWhiteSpace(PriceIdr))
        {
            if (Money.TryParseIdr(PriceIdr, out var parsed))
            {
                price = parsed;
            }
            else
            {
                priceProblem = new Problem(ExamValidator.PriceInvalid, ExamValidator.PriceField);
            }
        }

        var request = new SaveExamRequest(id, Title, Description, SubjectId, IsFree, price, AccessDays, OwnQuestionAllowance, RulePresetId);
        return (request, priceProblem);
    }
}

/// <summary>What <c>_ExamFormPartial</c> needs besides the posted fields.</summary>
public sealed record ExamFormView(ExamInput Input, IReadOnlyList<SubjectOption> Subjects, IReadOnlyList<RulePresetOption> Presets);
