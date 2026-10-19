using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Exams;

public sealed record ExamQuestionItem(
    long QuestionId,
    int Position,
    string Text,
    string SubjectName,
    string TopicName,
    QuestionType Type,
    QuestionStatus Status,
    bool IsTrial);

public sealed record TopicCount(string SubjectName, string TopicName, int Total, int Trial);

/// <summary>An exam's questions in order, with the counters shown on the picker (ADM-T4).</summary>
public sealed record ExamQuestionsView(long ExamId, bool IsFree, IReadOnlyList<ExamQuestionItem> Items)
{
    public int Total => Items.Count;

    public int TrialCount => Items.Count(i => i.IsTrial);

    /// <summary>Questions that won't be used in new attempts (Draft or Retired).</summary>
    public int InactiveCount => Items.Count(i => i.Status != QuestionStatus.Active);

    /// <summary>D-08, for paid exams.</summary>
    public TrialRequirement TrialRequirement => TrialSetRule.For(Total);

    public IReadOnlyList<TopicCount> ByTopic =>
    [
        .. Items
            .GroupBy(i => (i.SubjectName, i.TopicName))
            .Select(g => new TopicCount(g.Key.SubjectName, g.Key.TopicName, g.Count(), g.Count(i => i.IsTrial)))
            .OrderByDescending(t => t.Total)
            .ThenBy(t => t.TopicName, StringComparer.Ordinal),
    ];
}

public sealed class GetExamQuestions(IAppDbContext db)
{
    public async Task<ExamQuestionsView?> HandleAsync(long examId, CancellationToken cancellationToken)
    {
        var exam = await db.Exams
            .Where(e => e.Id == examId && e.OwnerKind == OwnerKind.App)
            .Select(e => new { e.Id, e.IsFree })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (exam is null)
        {
            return null;
        }

        var items = await db.ExamQuestions
            .Where(eq => eq.ExamId == examId)
            .OrderBy(eq => eq.SortOrder)
            .ThenBy(eq => eq.QuestionId)
            .Select(eq => new ExamQuestionItem(
                eq.QuestionId,
                eq.SortOrder,
                eq.Question!.Text,
                eq.Question.Topic!.Subject!.Name,
                eq.Question.Topic.Name,
                eq.Question.QuestionType,
                eq.Question.Status,
                eq.IsTrial))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new ExamQuestionsView(exam.Id, exam.IsFree, items);
    }
}
