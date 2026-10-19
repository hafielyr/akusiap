using Akusiap.Application.Questions;
using Akusiap.Application.Questions.Import;
using Akusiap.Domain.Content;

namespace Akusiap.UnitTests.Application;

/// <summary>ADM-T7: turning a file row into a question request and its problems.</summary>
public sealed class QuestionImportMapperTests
{
    private static readonly ImportTopicDirectory Topics = new(
    [
        new ImportTopic(1, "Aljabar", "Matematika"),
        new ImportTopic(2, "Umum", "Matematika"),
        new ImportTopic(3, "Umum", "Bahasa"),
    ]);

    [Fact]
    public void A_valid_single_choice_row_maps_to_a_request()
    {
        var (request, problems) = Map(Row(type: "SingleChoice", options: ["2", "3", "4"], correct: "B", difficulty: "3"));

        Assert.Empty(problems);
        Assert.Equal((1L, QuestionType.SingleChoice, (short?)3, QuestionStatus.Active), (request.TopicId, request.Type!.Value, request.Difficulty, request.Status!.Value));
        Assert.Equal([false, true, false], request.Options.Select(o => o.IsCorrect));
        Assert.Equal(["2", "3", "4"], request.Options.Select(o => o.Text));
    }

    [Theory]
    [InlineData("A,C")]
    [InlineData("A C")]
    [InlineData("ac")]
    [InlineData("A;C")]
    public void Multi_select_takes_several_letters(string correct)
    {
        var (request, problems) = Map(Row(type: "MultiSelect", options: ["a", "b", "c", "d"], correct: correct));

        Assert.Empty(problems);
        Assert.Equal([true, false, true, false], request.Options.Select(o => o.IsCorrect));
    }

    [Theory]
    [InlineData("pilihan ganda", QuestionType.SingleChoice)]
    [InlineData("PGK", QuestionType.MultiSelect)]
    [InlineData("Benar/Salah", QuestionType.TrueFalse)]
    [InlineData("truefalse", QuestionType.TrueFalse)]
    public void Indonesian_type_names_are_accepted(string type, QuestionType expected)
    {
        var options = expected == QuestionType.TrueFalse ? Array.Empty<string?>() : ["x", "y"];

        Assert.Equal(expected, Map(Row(type: type, options: options, correct: "A")).Request.Type);
    }

    [Theory]
    [InlineData("Benar", true)]
    [InlineData("salah", false)]
    [InlineData("A", true)]
    [InlineData("B", false)]
    public void True_false_rows_may_leave_options_empty(string correct, bool isTrue)
    {
        var (request, problems) = Map(Row(type: "TrueFalse", options: [], correct: correct));

        Assert.Empty(problems);
        Assert.Equal(["Benar", "Salah"], request.Options.Select(o => o.Text));
        Assert.Equal([isTrue, !isTrue], request.Options.Select(o => o.IsCorrect));
    }

    [Fact]
    public void Topics_are_found_by_name_or_by_subject_and_name()
    {
        Assert.Equal(1, Map(Row(topic: "aljabar")).Request.TopicId);
        Assert.Equal(3, Map(Row(topic: "Bahasa / Umum")).Request.TopicId);
        Assert.Equal([QuestionImportMapper.TopicAmbiguous], Map(Row(topic: "Umum")).Problems.Select(p => p.Code));
        Assert.Equal([QuestionImportMapper.TopicUnknown], Map(Row(topic: "Fisika")).Problems.Select(p => p.Code));
        Assert.Equal([QuestionImportMapper.TopicUnknown], Map(Row(topic: "Fisika / Umum")).Problems.Select(p => p.Code));
        Assert.Equal([QuestionValidator.TopicRequired], Map(Row(topic: " ")).Problems.Select(p => p.Code));
    }

    [Fact]
    public void Bad_correct_letters_are_reported()
    {
        Assert.Equal([QuestionImportMapper.CorrectInvalid], Map(Row(correct: "1")).Problems.Select(p => p.Code));
        var noOption = Assert.Single(Map(Row(options: ["a", "b"], correct: "D")).Problems);
        Assert.Equal((QuestionImportMapper.CorrectNoOption, "D"), (noOption.Code, (string)noOption.Args[0]));
    }

    [Fact]
    public void Validator_problems_come_through_together()
    {
        var problems = Map(Row(type: "Esai", text: " ", explanation: null, difficulty: "sulit")).Problems.Select(p => p.Code).ToList();

        Assert.Contains(QuestionValidator.TypeRequired, problems);
        Assert.Contains(QuestionValidator.DifficultyRange, problems);
        Assert.Equal(2, problems.Count(c => c == "Validation.Required")); // text and explanation
    }

    [Fact]
    public void A_gap_between_options_is_an_empty_option()
    {
        var problems = Map(Row(options: ["a", null, "c"], correct: "A")).Problems;

        var gap = Assert.Single(problems);
        Assert.Equal((QuestionValidator.OptionTextRequired, 2), (gap.Code, (int)gap.Args[0]));
    }

    private static (SaveQuestionRequest Request, List<Akusiap.Application.Common.Problem> Problems) Map(QuestionImportRow row) =>
        QuestionImportMapper.Map(row, Topics, QuestionStatus.Active);

    private static QuestionImportRow Row(
        string? type = "SingleChoice",
        string? text = "Soal?",
        string?[]? options = null,
        string? correct = "A",
        string? explanation = "Pembahasan.",
        string? topic = "Aljabar",
        string? difficulty = null)
    {
        var cells = new string?[QuestionImportColumns.MaxOptions];
        (options ?? ["satu", "dua"]).CopyTo(cells, 0);
        return new QuestionImportRow(2, type, text, cells, correct, explanation, null, null, topic, difficulty, null);
    }
}
