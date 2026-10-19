using Akusiap.Application.Common;
using Akusiap.Application.Exams;

namespace Akusiap.UnitTests.Application;

/// <summary>ADM-T3: what an app exam needs before it is saved.</summary>
public sealed class ExamValidatorTests
{
    [Fact]
    public void Valid_paid_and_free_exams_have_no_problems()
    {
        Assert.Empty(ExamValidator.Validate(Paid()));
        Assert.Empty(ExamValidator.Validate(Free()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Paid_exam_needs_a_price_above_zero(long? price)
    {
        var problem = Assert.Single(ExamValidator.Validate(Paid() with { PriceIdr = price }));

        Assert.Equal((ExamValidator.PriceRequired, ExamValidator.PriceField), (problem.Code, problem.Field));
    }

    [Fact]
    public void Price_has_a_sane_upper_limit()
    {
        Assert.Empty(ExamValidator.Validate(Paid() with { PriceIdr = ExamValidator.MaxPriceIdr }));

        var problem = Assert.Single(ExamValidator.Validate(Paid() with { PriceIdr = ExamValidator.MaxPriceIdr + 1 }));
        Assert.Equal(ExamValidator.PriceTooHigh, problem.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-30)]
    public void Paid_exam_needs_access_days_above_zero(int? days)
    {
        var problem = Assert.Single(ExamValidator.Validate(Paid() with { AccessDays = days }));

        Assert.Equal((ExamValidator.AccessDaysRequired, ExamValidator.AccessDaysField), (problem.Code, problem.Field));
    }

    [Fact]
    public void Access_days_have_an_upper_limit()
    {
        Assert.Empty(ExamValidator.Validate(Paid() with { AccessDays = ExamValidator.MaxAccessDays }));
        Assert.Equal(ExamValidator.AccessDaysTooHigh, Assert.Single(ExamValidator.Validate(Paid() with { AccessDays = ExamValidator.MaxAccessDays + 1 })).Code);
    }

    [Fact]
    public void Free_exam_ignores_price_and_access_days()
    {
        Assert.Empty(ExamValidator.Validate(Free() with { PriceIdr = -5, AccessDays = 0 }));
        Assert.Empty(ExamValidator.Validate(Free() with { PriceIdr = null, AccessDays = null }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Title_is_required(string? title)
    {
        var problem = Assert.Single(ExamValidator.Validate(Paid() with { Title = title }));

        Assert.Equal((TextRules.Required, ExamValidator.TitleField), (problem.Code, problem.Field));
    }

    [Fact]
    public void Title_and_description_have_length_limits()
    {
        Assert.Empty(ExamValidator.Validate(Paid() with { Title = new string('t', 200), Description = new string('d', 4000) }));

        var problems = ExamValidator.Validate(Paid() with { Title = new string('t', 201), Description = new string('d', 4001) });
        Assert.Equal(
            new[] { (TextRules.TooLong, ExamValidator.TitleField), (TextRules.TooLong, ExamValidator.DescriptionField) },
            problems.Select(p => (p.Code, p.Field!)).ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(ExamValidator.MaxAllowance + 1)]
    public void Own_question_allowance_is_0_to_the_maximum(int allowance)
    {
        var problem = Assert.Single(ExamValidator.Validate(Paid() with { OwnQuestionAllowance = allowance }));

        Assert.Equal((ExamValidator.AllowanceRange, ExamValidator.AllowanceField), (problem.Code, problem.Field));
    }

    [Fact]
    public void A_rule_preset_is_required()
    {
        var problem = Assert.Single(ExamValidator.Validate(Paid() with { RulePresetId = 0 }));

        Assert.Equal((ExamValidator.RulePresetRequired, ExamValidator.RulePresetField), (problem.Code, problem.Field));
    }

    [Fact]
    public void Subject_is_optional()
    {
        Assert.Empty(ExamValidator.Validate(Paid() with { SubjectId = null }));
    }

    private static SaveExamRequest Paid() => new(
        Id: null,
        Title: "Tryout SKD CPNS 1",
        Description: "40 soal TWK, TIU dan TKP.",
        SubjectId: 3,
        IsFree: false,
        PriceIdr: 49_000,
        AccessDays: 30,
        OwnQuestionAllowance: 100,
        RulePresetId: 1);

    private static SaveExamRequest Free() => Paid() with { IsFree = true, PriceIdr = null, AccessDays = null, OwnQuestionAllowance = 0 };
}
