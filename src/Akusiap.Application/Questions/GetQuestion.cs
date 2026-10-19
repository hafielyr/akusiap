using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Questions;

public sealed record OptionDetail(long Id, string Text, bool IsCorrect);

public sealed record QuestionDetail(
    long Id,
    long TopicId,
    QuestionType Type,
    string Text,
    string Explanation,
    string? KeyFact,
    string? Hint,
    short? Difficulty,
    string? SourceNote,
    QuestionStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int ExamCount,
    IReadOnlyList<OptionDetail> Options);

/// <summary>An app question with its options in order, for the editor.</summary>
public sealed class GetQuestion(IAppDbContext db)
{
    public async Task<QuestionDetail?> HandleAsync(long id, CancellationToken cancellationToken) =>
        await db.Questions
            .Where(q => q.Id == id && q.OwnerUserId == null)
            .Select(q => new QuestionDetail(
                q.Id,
                q.TopicId,
                q.QuestionType,
                q.Text,
                q.Explanation,
                q.KeyFact,
                q.Hint,
                q.Difficulty,
                q.SourceNote,
                q.Status,
                q.CreatedAt,
                q.UpdatedAt,
                db.ExamQuestions.Count(eq => eq.QuestionId == q.Id),
                q.Options.OrderBy(o => o.SortOrder).Select(o => new OptionDetail(o.Id, o.Text, o.IsCorrect)).ToList()))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}
