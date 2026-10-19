using Akusiap.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Topics;

public sealed record TopicDetail(long Id, long SubjectId, string SubjectName, string Name, string? Description, int QuestionCount);

public sealed class GetTopic(IAppDbContext db)
{
    public async Task<TopicDetail?> HandleAsync(long id, CancellationToken cancellationToken) =>
        await db.Topics
            .Where(t => t.Id == id)
            .Select(t => new TopicDetail(
                t.Id,
                t.SubjectId,
                t.Subject!.Name,
                t.Name,
                t.Description,
                db.Questions.Count(q => q.TopicId == t.Id)))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}
