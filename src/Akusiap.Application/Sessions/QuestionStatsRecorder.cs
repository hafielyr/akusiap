using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Sessions;

/// <summary>
/// Adds a finished session's answers to each question's stats (DIF-01, D-140): right or wrong for answered items (blanks
/// don't count), and marked for items marked at any point. Flashcard sets are left out: they repeat questions the
/// learner already got wrong. Rows are created when missing, then raised with atomic updates, so two sessions ending at
/// once don't lose counts.
/// </summary>
public sealed class QuestionStatsRecorder(IAppDbContext db)
{
    public async Task RecordAsync(Attempt attempt, IReadOnlyCollection<AttemptItem> items, DateTime at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(items);
        if (attempt.Mode == AttemptMode.Flashcard)
        {
            return;
        }

        var counted = items
            .Select(i => (i.QuestionId, Correct: i.IsAnswered && i.IsCorrect ? 1 : 0, Wrong: i.IsAnswered && !i.IsCorrect ? 1 : 0, Marked: i.WasFlagged ? 1 : 0))
            .Where(c => c.Correct + c.Wrong + c.Marked > 0)
            .ToList();
        if (counted.Count == 0)
        {
            return;
        }

        var ids = counted.Select(c => c.QuestionId).ToList();
        var existing = await db.QuestionStats.Where(s => ids.Contains(s.QuestionId)).Select(s => s.QuestionId).ToListAsync(cancellationToken).ConfigureAwait(false);
        var missing = ids.Except(existing).ToList();
        if (missing.Count > 0)
        {
            var rows = missing.Select(id => new QuestionStats { QuestionId = id, UpdatedAt = at }).ToList();
            db.QuestionStats.AddRange(rows);
            try
            {
                await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateException)
            {
                // Another session created some of these rows first; theirs are used below. Removing an added row detaches it.
                db.QuestionStats.RemoveRange(rows);
            }
        }

        foreach (var group in counted.GroupBy(c => (c.Correct, c.Wrong, c.Marked)))
        {
            var (correct, wrong, marked) = group.Key;
            var groupIds = group.Select(c => c.QuestionId).ToList();
            await db.QuestionStats
                .Where(s => groupIds.Contains(s.QuestionId))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(x => x.CorrectCount, x => x.CorrectCount + correct)
                        .SetProperty(x => x.WrongCount, x => x.WrongCount + wrong)
                        .SetProperty(x => x.MarkedCount, x => x.MarkedCount + marked)
                        .SetProperty(x => x.UpdatedAt, at),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
