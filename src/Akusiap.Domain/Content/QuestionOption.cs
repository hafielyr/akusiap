namespace Akusiap.Domain.Content;

public class QuestionOption
{
    public int QuestionOptionId { get; set; }

    public int QuestionId { get; set; }

    public Question? Question { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }

    public int SortOrder { get; set; }
}
