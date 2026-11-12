using Akusiap.Application.Abstractions;
using Akusiap.Domain;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Planning;

/// <summary>
/// The latest a review card may be due (PLN-03, D-123): the start of the Jakarta day before the soonest upcoming
/// exam goal whose exam holds the card's question. Null when no active goal's exam holds it.
/// </summary>
public sealed class ExamDayCap(IAppDbContext db)
{
    public async Task<DateTime?> ForQuestionAsync(Guid userId, long questionId, DateTime now, CancellationToken cancellationToken)
    {
        var today = JakartaTime.Today(now);
        var examDate = await db.StudyGoals.AsNoTracking()
            .Where(g => g.UserId == userId && g.ArchivedAt == null && g.ExamDate > today && g.Exam!.Questions.Any(q => q.QuestionId == questionId))
            .OrderBy(g => g.ExamDate)
            .Select(g => (DateOnly?)g.ExamDate)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return examDate is { } date ? JakartaTime.StartOfDayUtc(date.AddDays(-1)) : null;
    }
}
