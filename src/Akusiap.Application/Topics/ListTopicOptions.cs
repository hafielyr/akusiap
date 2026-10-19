using Akusiap.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Topics;

public sealed record TopicOption(long Id, string Name, long SubjectId, string SubjectName);

/// <summary>Every topic with its subject, ordered for a grouped dropdown.</summary>
public sealed class ListTopicOptions(IAppDbContext db)
{
    public async Task<IReadOnlyList<TopicOption>> HandleAsync(CancellationToken cancellationToken) =>
        await db.Topics
            .OrderBy(t => t.Subject!.Name)
            .ThenBy(t => t.Name)
            .Select(t => new TopicOption(t.Id, t.Name, t.SubjectId, t.Subject!.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
