using Akusiap.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Subjects;

/// <summary>One subject with its topics and how many questions use each topic.</summary>
public sealed class GetSubject(IAppDbContext db)
{
    public async Task<SubjectDetail?> HandleAsync(long id, CancellationToken cancellationToken)
    {
        var subject = await db.Subjects
            .Where(s => s.Id == id)
            .Select(s => new { s.Id, s.Name, s.Description, ExamCount = db.Exams.Count(e => e.SubjectId == s.Id) })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (subject is null)
        {
            return null;
        }

        var topics = await db.Topics
            .Where(t => t.SubjectId == id)
            .OrderBy(t => t.Name)
            .Select(t => new TopicSummary(t.Id, t.Name, t.Description, db.Questions.Count(q => q.TopicId == t.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new SubjectDetail(subject.Id, subject.Name, subject.Description, subject.ExamCount, topics);
    }
}
