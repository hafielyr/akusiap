namespace Akusiap.Domain.Content;

public class Subject
{
    public int SubjectId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public ICollection<Topic> Topics { get; } = [];
}
