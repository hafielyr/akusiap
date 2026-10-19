using System.Text.Json;
using Akusiap.Application.Questions;
using Akusiap.Application.Topics;
using Akusiap.Domain.Content;

namespace Akusiap.Web.Areas.Admin.Pages.Questions;

/// <summary>Question form fields, posted as <c>Input.*</c>. Rules live in <see cref="QuestionValidator"/>.</summary>
public sealed class QuestionInput
{
    public long TopicId { get; set; }

    public QuestionType? QuestionType { get; set; } = Domain.Content.QuestionType.SingleChoice;

    public string? Text { get; set; }

    public string? Explanation { get; set; }

    public string? KeyFact { get; set; }

    public string? Hint { get; set; }

    public short? Difficulty { get; set; }

    public string? SourceNote { get; set; }

    public QuestionStatus? Status { get; set; } = QuestionStatus.Draft;

    public List<OptionInput> Options { get; set; } = [];

    /// <summary>A new question: four empty single-choice options, optionally in a given topic.</summary>
    public static QuestionInput Blank(long topicId) => new()
    {
        TopicId = topicId,
        Options = [new(), new(), new(), new()],
    };

    public static QuestionInput From(QuestionDetail question)
    {
        ArgumentNullException.ThrowIfNull(question);
        return new QuestionInput
        {
            TopicId = question.TopicId,
            QuestionType = question.Type,
            Text = question.Text,
            Explanation = question.Explanation,
            KeyFact = question.KeyFact,
            Hint = question.Hint,
            Difficulty = question.Difficulty,
            SourceNote = question.SourceNote,
            Status = question.Status,
            Options = question.Options.Select(o => new OptionInput { Id = o.Id, Text = o.Text, IsCorrect = o.IsCorrect }).ToList(),
        };
    }

    public SaveQuestionRequest ToRequest(long? id) => new(
        id,
        TopicId,
        QuestionType,
        Text,
        Explanation,
        KeyFact,
        Hint,
        Difficulty,
        SourceNote,
        Status,
        Options.Select(o => new OptionRequest(o.Id, o.Text, o.IsCorrect)).ToList());
}

public sealed class OptionInput
{
    public long? Id { get; set; }

    public string? Text { get; set; }

    public bool IsCorrect { get; set; }
}

/// <summary>What <c>_QuestionFormPartial</c> needs besides the posted fields.</summary>
public sealed record QuestionFormView(QuestionInput Input, IReadOnlyList<TopicOption> Topics, bool IsNew)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Initial state for the Alpine option editor (wwwroot/js/question-form.js).</summary>
    public string InitialStateJson => JsonSerializer.Serialize(
        new
        {
            type = (Input.QuestionType ?? QuestionType.SingleChoice).ToString(),
            options = Input.Options.Select(o => new { id = o.Id, text = o.Text ?? string.Empty, isCorrect = o.IsCorrect }),
            trueText = QuestionValidator.TrueText,
            falseText = QuestionValidator.FalseText,
            maxOptions = Enum.GetValues<QuestionType>().ToDictionary(t => t.ToString(), t => QuestionValidator.OptionCountFor(t).Max),
        },
        Json);
}
