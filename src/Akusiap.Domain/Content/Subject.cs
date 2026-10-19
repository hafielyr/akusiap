namespace Akusiap.Domain.Content;

public class Subject
{
    public long Id { get; set; }

    /// <summary>Unique. Formerly "Group".</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ICollection<Topic> Topics { get; } = [];
}
