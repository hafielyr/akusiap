using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Exams;

public sealed record QuestionCandidate(long QuestionId, string Text, string SubjectName, string TopicName, QuestionType Type, bool AlreadyAdded);

/// <summary>Active app questions matching a text and/or topic, marking the ones already in the exam (ADM-T4).</summary>
public sealed class SearchQuestionsForExam(IAppDbContext db)
{
    public const int MaxResults = 20;

    public async Task<IReadOnlyList<QuestionCandidate>> HandleAsync(
        long examId, string? search, long? topicId, CancellationToken cancellationToken)
    {
        var query = db.Questions.Where(q => q.OwnerUserId == null && q.Status == QuestionStatus.Active);
        if (topicId is { } topic)
        {
            query = query.Where(q => q.TopicId == topic);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
#pragma warning disable CA1304, CA1311, CA1862 // ToLower() is translated to SQL lower(); culture overloads are not.
            var term = search.Trim().ToLower();
            query = query.Where(q => q.Text.ToLower().Contains(term));
#pragma warning restore CA1304, CA1311, CA1862
        }

        return await query
            .OrderByDescending(q => q.UpdatedAt)
            .ThenByDescending(q => q.Id)
            .Take(MaxResults)
            .Select(q => new QuestionCandidate(
                q.Id,
                q.Text,
                q.Topic!.Subject!.Name,
                q.Topic.Name,
                q.QuestionType,
                db.ExamQuestions.Any(eq => eq.ExamId == examId && eq.QuestionId == q.Id)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
