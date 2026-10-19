namespace Akusiap.Domain.Content;

public class Topic
{
    public long Id { get; set; }

    public long SubjectId { get; set; }

    public Subject? Subject { get; set; }

    /// <summary>Unique within the subject. Formerly "Type".</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}
