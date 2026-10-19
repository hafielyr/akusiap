using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Subjects;

/// <summary>Deletes a subject that has no topics and no exams; otherwise says what still uses it.</summary>
public sealed class DeleteSubject(IAppDbContext db)
{
    public async Task<Result<bool>> HandleAsync(long id, CancellationToken cancellationToken)
    {
        var subject = await db.Subjects.SingleOrDefaultAsync(s => s.Id == id, cancellationToken).ConfigureAwait(false);
        if (subject is null)
        {
            return Result.Fail<bool>(Problem.NotFound);
        }

        var topics = await db.Topics.CountAsync(t => t.SubjectId == id, cancellationToken).ConfigureAwait(false);
        var exams = await db.Exams.CountAsync(e => e.SubjectId == id, cancellationToken).ConfigureAwait(false);
        if (topics > 0 || exams > 0)
        {
            return Result.Fail<bool>(new Problem(SubjectRules.InUse, null, topics, exams));
        }

        db.Subjects.Remove(subject);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(true);
    }
}
