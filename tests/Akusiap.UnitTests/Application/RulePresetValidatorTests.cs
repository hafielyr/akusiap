using Akusiap.Application.Common;
using Akusiap.Application.RulePresets;
using Akusiap.Domain.Content;

namespace Akusiap.UnitTests.Application;

/// <summary>ADM-T5: what a system rule preset needs before it is saved.</summary>
public sealed class RulePresetValidatorTests
{
    [Fact]
    public void A_typical_preset_is_valid()
    {
        Assert.Empty(RulePresetValidator.Validate(Valid()));
    }

    [Fact]
    public void Blank_limits_mean_untimed_all_questions_unlimited_attempts_and_no_pass_mark()
    {
        Assert.Empty(RulePresetValidator.Validate(Valid() with { TimeLimitMinutes = null, QuestionCount = null, MaxAttempts = null, PassMarkPercent = null }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Name_is_required(string? name)
    {
        var problem = Assert.Single(RulePresetValidator.Validate(Valid() with { Name = name }));

        Assert.Equal((TextRules.Required, RulePresetValidator.NameField), (problem.Code, problem.Field));
    }

    [Theory]
    [InlineData(0, RulePresetValidator.TimeLimitField)]
    [InlineData(RulePresetValidator.MaxTimeLimitMinutes + 1, RulePresetValidator.TimeLimitField)]
    public void Time_limit_is_1_to_600_minutes(int minutes, string field)
    {
        var problem = Assert.Single(RulePresetValidator.Validate(Valid() with { TimeLimitMinutes = minutes }));

        Assert.Equal((RulePresetValidator.RangeCode, field), (problem.Code, problem.Field));
        Assert.Equal(new object[] { RulePresetValidator.TimeLimitLabel, 1, RulePresetValidator.MaxTimeLimitMinutes }, problem.Args);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(RulePresetValidator.MaxQuestionCount + 1)]
    public void Question_count_is_1_to_the_maximum(int count)
    {
        var problem = Assert.Single(RulePresetValidator.Validate(Valid() with { QuestionCount = count }));

        Assert.Equal((RulePresetValidator.RangeCode, RulePresetValidator.QuestionCountField), (problem.Code, problem.Field));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(RulePresetValidator.MaxAttemptsLimit + 1)]
    public void Max_attempts_is_1_to_the_maximum(int attempts)
    {
        var problem = Assert.Single(RulePresetValidator.Validate(Valid() with { MaxAttempts = attempts }));

        Assert.Equal((RulePresetValidator.RangeCode, RulePresetValidator.MaxAttemptsField), (problem.Code, problem.Field));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(100, true)]
    [InlineData(-1, false)]
    [InlineData(101, false)]
    public void Pass_mark_is_0_to_100_percent(int percent, bool valid)
    {
        var problems = RulePresetValidator.Validate(Valid() with { PassMarkPercent = percent });

        Assert.Equal(valid, problems.Count == 0);
    }

    [Fact]
    public void Show_answers_must_be_chosen()
    {
        var problem = Assert.Single(RulePresetValidator.Validate(Valid() with { ShowAnswers = null }));

        Assert.Equal((RulePresetValidator.ShowAnswersRequired, RulePresetValidator.ShowAnswersField), (problem.Code, problem.Field));
    }

    [Fact]
    public void Every_problem_is_reported_at_once()
    {
        var problems = RulePresetValidator.Validate(Valid() with { Name = "", TimeLimitMinutes = 0, PassMarkPercent = 200, ShowAnswers = null });

        Assert.Equal(4, problems.Count);
    }

    private static SaveRulePresetRequest Valid() => new(
        Id: null,
        Name: "Simulasi SKD",
        TimeLimitMinutes: 100,
        QuestionCount: 110,
        HintsEnabled: false,
        ShuffleQuestions: true,
        ShuffleOptions: true,
        MaxAttempts: 3,
        PassMarkPercent: 65,
        ShowAnswers: ShowAnswers.AfterSubmit,
        IsDefault: false);
}
