using Akusiap.Domain.Content;

namespace Akusiap.Domain.Sessions;

/// <summary>An option the learner selected; several per item for MultiSelect.</summary>
public class AttemptItemOption
{
    public long AttemptItemId { get; set; }

    public AttemptItem? AttemptItem { get; set; }

    public long QuestionOptionId { get; set; }

    public QuestionOption? QuestionOption { get; set; }
}
