using Akusiap.Application.Common;
using Akusiap.Application.Questions;
using Akusiap.Domain.Content;

namespace Akusiap.UnitTests.Application;

/// <summary>ADM-T2: QuestionValidator per question type, and the shared field rules.</summary>
public sealed class QuestionValidatorTests
{
    // ---------- SingleChoice: 2–6 options, exactly 1 correct ----------

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(6)]
    public void SingleChoice_with_2_to_6_options_and_one_correct_is_valid(int count)
    {
        Assert.Empty(QuestionValidator.Validate(Request(QuestionType.SingleChoice, Options(count, correct: [0]))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    public void SingleChoice_needs_2_to_6_options(int count)
    {
        var problems = QuestionValidator.Validate(Request(QuestionType.SingleChoice, Options(count, correct: count > 0 ? [0] : [])));

        var problem = Assert.Single(problems, p => p.Code == QuestionValidator.OptionCount);
        Assert.Equal(new object[] { "Admin.QuestionTypeName.SingleChoice", 2, 6 }, problem.Args);
        Assert.Equal(QuestionValidator.OptionsField, problem.Field);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 0, 1 })]
    public void SingleChoice_needs_exactly_one_correct_option(int[] correct)
    {
        var problem = Assert.Single(QuestionValidator.Validate(Request(QuestionType.SingleChoice, Options(4, correct))));

        Assert.Equal(QuestionValidator.ExactlyOneCorrect, problem.Code);
    }

    // ---------- MultiSelect: 2–8 options, at least 1 correct ----------

    [Theory]
    [InlineData(2, new[] { 0 })]
    [InlineData(5, new[] { 0, 2, 4 })]
    [InlineData(8, new[] { 0, 1, 2, 3, 4, 5, 6, 7 })]
    public void MultiSelect_with_2_to_8_options_and_any_correct_is_valid(int count, int[] correct)
    {
        Assert.Empty(QuestionValidator.Validate(Request(QuestionType.MultiSelect, Options(count, correct))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void MultiSelect_needs_2_to_8_options(int count)
    {
        var problem = Assert.Single(QuestionValidator.Validate(Request(QuestionType.MultiSelect, Options(count, correct: [0]))));

        Assert.Equal(QuestionValidator.OptionCount, problem.Code);
        Assert.Equal(new object[] { "Admin.QuestionTypeName.MultiSelect", 2, 8 }, problem.Args);
    }

    [Fact]
    public void MultiSelect_needs_at_least_one_correct_option()
    {
        var problem = Assert.Single(QuestionValidator.Validate(Request(QuestionType.MultiSelect, Options(4, correct: []))));

        Assert.Equal(QuestionValidator.AtLeastOneCorrect, problem.Code);
    }

    // ---------- TrueFalse: exactly "Benar" and "Salah", 1 correct ----------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrueFalse_with_benar_and_salah_and_one_correct_is_valid(bool trueIsCorrect)
    {
        var options = new[] { new OptionRequest(null, "Benar", trueIsCorrect), new OptionRequest(null, "Salah", !trueIsCorrect) };

        Assert.Empty(QuestionValidator.Validate(Request(QuestionType.TrueFalse, options)));
    }

    [Fact]
    public void TrueFalse_needs_exactly_the_two_fixed_options()
    {
        var wrongTexts = new[] { new OptionRequest(null, "Ya", true), new OptionRequest(null, "Tidak", false) };
        var swapped = new[] { new OptionRequest(null, "Salah", true), new OptionRequest(null, "Benar", false) };
        var three = new[] { new OptionRequest(null, "Benar", true), new OptionRequest(null, "Salah", false), new OptionRequest(null, "Mungkin", false) };

        Assert.Contains(QuestionValidator.Validate(Request(QuestionType.TrueFalse, wrongTexts)), p => p.Code == QuestionValidator.TrueFalseOptions);
        Assert.Contains(QuestionValidator.Validate(Request(QuestionType.TrueFalse, swapped)), p => p.Code == QuestionValidator.TrueFalseOptions);
        Assert.Contains(QuestionValidator.Validate(Request(QuestionType.TrueFalse, three)), p => p.Code == QuestionValidator.TrueFalseOptions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void TrueFalse_needs_exactly_one_correct_option(bool trueIsCorrect, bool falseIsCorrect)
    {
        var options = new[] { new OptionRequest(null, "Benar", trueIsCorrect), new OptionRequest(null, "Salah", falseIsCorrect) };

        var problem = Assert.Single(QuestionValidator.Validate(Request(QuestionType.TrueFalse, options)));
        Assert.Equal(QuestionValidator.ExactlyOneCorrect, problem.Code);
    }

    // ---------- Options in general ----------

    [Fact]
    public void Every_option_needs_text_and_the_message_names_its_number()
    {
        var options = new[] { new OptionRequest(null, "A", true), new OptionRequest(null, "  ", false), new OptionRequest(null, "C", false) };

        var problem = Assert.Single(QuestionValidator.Validate(Request(QuestionType.SingleChoice, options)));

        Assert.Equal(QuestionValidator.OptionTextRequired, problem.Code);
        Assert.Equal(new object[] { 2 }, problem.Args);
    }

    [Fact]
    public void Option_text_may_be_1000_characters_but_not_more()
    {
        var ok = new[] { new OptionRequest(null, new string('a', 1000), true), new OptionRequest(null, "B", false) };
        var tooLong = new[] { new OptionRequest(null, new string('a', 1001), true), new OptionRequest(null, "B", false) };

        Assert.Empty(QuestionValidator.Validate(Request(QuestionType.SingleChoice, ok)));
        Assert.Equal(QuestionValidator.OptionTooLong, Assert.Single(QuestionValidator.Validate(Request(QuestionType.SingleChoice, tooLong))).Code);
    }

    [Fact]
    public void Options_must_differ_ignoring_case_and_spaces()
    {
        var options = new[] { new OptionRequest(null, "Jakarta", true), new OptionRequest(null, " jakarta ", false) };

        var problem = Assert.Single(QuestionValidator.Validate(Request(QuestionType.SingleChoice, options)));

        Assert.Equal(QuestionValidator.OptionDuplicate, problem.Code);
        Assert.Equal(new object[] { 2 }, problem.Args);
    }

    // ---------- Fields ----------

    [Fact]
    public void Text_and_explanation_are_required()
    {
        var problems = QuestionValidator.Validate(Request(QuestionType.SingleChoice, Options(2, [0])) with { Text = " ", Explanation = null });

        Assert.Contains(problems, p => p is { Code: TextRules.Required, Field: QuestionValidator.TextField });
        Assert.Contains(problems, p => p is { Code: TextRules.Required, Field: QuestionValidator.ExplanationField });
    }

    [Fact]
    public void Key_fact_is_optional_up_to_200_characters()
    {
        var valid = Request(QuestionType.SingleChoice, Options(2, [0]));

        Assert.Empty(QuestionValidator.Validate(valid with { KeyFact = null }));
        Assert.Empty(QuestionValidator.Validate(valid with { KeyFact = new string('k', 200) }));
        var problem = Assert.Single(QuestionValidator.Validate(valid with { KeyFact = new string('k', 201) }));
        Assert.Equal((TextRules.TooLong, QuestionValidator.KeyFactField), (problem.Code, problem.Field));
        Assert.Equal(200, problem.Args[1]);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)6)]
    public void Difficulty_must_be_1_to_5_when_given(short difficulty)
    {
        var problem = Assert.Single(QuestionValidator.Validate(Request(QuestionType.SingleChoice, Options(2, [0])) with { Difficulty = difficulty }));

        Assert.Equal((QuestionValidator.DifficultyRange, QuestionValidator.DifficultyField), (problem.Code, problem.Field));
    }

    [Theory]
    [InlineData(null)]
    [InlineData((short)1)]
    [InlineData((short)5)]
    public void Difficulty_may_be_empty_or_1_to_5(short? difficulty)
    {
        Assert.Empty(QuestionValidator.Validate(Request(QuestionType.SingleChoice, Options(2, [0])) with { Difficulty = difficulty }));
    }

    [Fact]
    public void Topic_type_and_status_are_required()
    {
        var problems = QuestionValidator.Validate(Request(QuestionType.SingleChoice, Options(2, [0])) with { TopicId = 0, Type = null, Status = null });

        Assert.Contains(problems, p => p is { Code: QuestionValidator.TopicRequired, Field: QuestionValidator.TopicField });
        Assert.Contains(problems, p => p is { Code: QuestionValidator.TypeRequired, Field: QuestionValidator.TypeField });
        Assert.Contains(problems, p => p is { Code: QuestionValidator.StatusRequired, Field: QuestionValidator.StatusField });
    }

    [Fact]
    public void Every_code_is_a_translation_key()
    {
        string[] codes =
        [
            QuestionValidator.TopicRequired, QuestionValidator.TypeRequired, QuestionValidator.StatusRequired,
            QuestionValidator.DifficultyRange, QuestionValidator.OptionCount, QuestionValidator.ExactlyOneCorrect,
            QuestionValidator.AtLeastOneCorrect, QuestionValidator.TrueFalseOptions, QuestionValidator.OptionTextRequired,
            QuestionValidator.OptionTooLong, QuestionValidator.OptionDuplicate,
        ];

        Assert.All(codes, code => Assert.StartsWith("Admin.Questions.", code, StringComparison.Ordinal));
    }

    // ---------- Helpers ----------

    private static SaveQuestionRequest Request(QuestionType type, IReadOnlyList<OptionRequest> options) => new(
        Id: null,
        TopicId: 1,
        Type: type,
        Text: "Ibu kota Indonesia adalah …",
        Explanation: "Jakarta, sampai pemindahan ke IKN.",
        KeyFact: "Ibu kota: Jakarta",
        Hint: null,
        Difficulty: 2,
        SourceNote: "Kisi-kisi TWK",
        Status: QuestionStatus.Active,
        Options: options);

    private static OptionRequest[] Options(int count, int[] correct) =>
        Enumerable.Range(0, count).Select(i => new OptionRequest(null, $"Pilihan {i + 1}", correct.Contains(i))).ToArray();
}
