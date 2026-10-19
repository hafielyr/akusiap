using Akusiap.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Subjects;

/// <summary>All subjects by name, with how many topics and exams use each (ADM-03).</summary>
public sealed class ListSubjects(IAppDbContext db)
{
    public async Task<IReadOnlyList<SubjectSummary>> HandleAsync(CancellationToken cancellationToken) =>
        await db.Subjects
            .OrderBy(s => s.Name)
            .Select(s => new SubjectSummary(
                s.Id,
                s.Name,
                s.Description,
                s.Topics.Count,
                db.Exams.Count(e => e.SubjectId == s.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<SubjectOption>> OptionsAsync(CancellationToken cancellationToken) =>
        await db.Subjects
            .OrderBy(s => s.Name)
            .Select(s => new SubjectOption(s.Id, s.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
