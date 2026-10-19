using Akusiap.Application.Abstractions;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Exams;

public sealed record ExamRow(
    long Id,
    string Title,
    string? SubjectName,
    ExamStatus Status,
    bool IsFree,
    long? PriceIdr,
    int? AccessDays,
    int QuestionCount,
    int TrialCount);

public sealed record ExamDetail(
    long Id,
    string Title,
    string? Description,
    long? SubjectId,
    bool IsFree,
    long? PriceIdr,
    int? AccessDays,
    int OwnQuestionAllowance,
    long RulePresetId,
    ExamStatus Status,
    DateTime CreatedAt,
    int QuestionCount,
    int TrialCount,
    int AttemptCount,
    int PurchaseCount);

/// <summary>App exams, newest first, with question and trial counts.</summary>
public sealed class ListExams(IAppDbContext db)
{
    public async Task<IReadOnlyList<ExamRow>> HandleAsync(CancellationToken cancellationToken) =>
        await db.Exams
            .Where(e => e.OwnerKind == OwnerKind.App)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new ExamRow(
                e.Id,
                e.Title,
                e.Subject == null ? null : e.Subject.Name,
                e.Status,
                e.IsFree,
                e.PriceIdr,
                e.AccessDays,
                e.Questions.Count,
                e.Questions.Count(q => q.IsTrial)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>One app exam with what uses it, for the editor.</summary>
public sealed class GetExam(IAppDbContext db)
{
    public async Task<ExamDetail?> HandleAsync(long id, CancellationToken cancellationToken) =>
        await db.Exams
            .Where(e => e.Id == id && e.OwnerKind == OwnerKind.App)
            .Select(e => new ExamDetail(
                e.Id,
                e.Title,
                e.Description,
                e.SubjectId,
                e.IsFree,
                e.PriceIdr,
                e.AccessDays,
                e.OwnQuestionAllowance,
                e.RulePresetId,
                e.Status,
                e.CreatedAt,
                e.Questions.Count,
                e.Questions.Count(q => q.IsTrial),
                db.Attempts.Count(a => a.ExamId == e.Id),
                db.ExamPurchases.Count(p => p.ExamId == e.Id)))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
}
