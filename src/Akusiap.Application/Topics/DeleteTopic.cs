using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Topics;

/// <summary>Deletes a topic no question uses (ADM-T1 AC); otherwise says how many questions use it.</summary>
public sealed class DeleteTopic(IAppDbContext db)
{
    /// <returns>The id of the topic's subject, so the page can go back to it.</returns>
    public async Task<Result<long>> HandleAsync(long id, CancellationToken cancellationToken)
    {
        var topic = await db.Topics.SingleOrDefaultAsync(t => t.Id == id, cancellationToken).ConfigureAwait(false);
        if (topic is null)
        {
            return Result.Fail<long>(Problem.NotFound);
        }

        var questions = await db.Questions.CountAsync(q => q.TopicId == id, cancellationToken).ConfigureAwait(false);
        if (questions > 0)
        {
            return Result.Fail<long>(new Problem(TopicRules.InUse, null, questions));
        }

        db.Topics.Remove(topic);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(topic.SubjectId);
    }
}
