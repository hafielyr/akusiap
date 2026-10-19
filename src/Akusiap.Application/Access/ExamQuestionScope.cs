using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Access;

/// <summary>
/// The question ids an access decision allows, in exam order (ENT-02): TrialOnly → trial questions that are Active;
/// All → every Active question; None → nothing. Draft and retired questions are never returned.
/// </summary>
public sealed class ExamQuestionScope(IAppDbContext db)
{
    public async Task<IReadOnlyList<long>> GetQuestionIdsAsync(long examId, QuestionScope scope, CancellationToken cancellationToken)
    {
        if (scope == QuestionScope.None)
        {
            return [];
        }

        var trialOnly = scope == QuestionScope.TrialOnly;
        return await db.ExamQuestions
            .Where(eq => eq.ExamId == examId && eq.Question!.Status == QuestionStatus.Active && (!trialOnly || eq.IsTrial))
            .OrderBy(eq => eq.SortOrder)
            .ThenBy(eq => eq.QuestionId)
            .Select(eq => eq.QuestionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
