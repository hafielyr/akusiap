namespace Akusiap.Domain.Content;

public class Topic
{
    public int TopicId { get; set; }

    public int SubjectId { get; set; }

    public Subject? Subject { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
