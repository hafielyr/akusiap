using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Exams;

/// <summary>
/// Publishes an app exam when <see cref="PublishValidator"/> finds no problems, and unpublishes a published one at any
/// time (ADM-11). Unpublished exams leave the catalog, and by the entitlement rules (rule 5 before rule 7) buyers lose
/// access until the exam is published again.
/// </summary>
public sealed class PublishExam(IAppDbContext db)
{
    public const string NotPublished = "Admin.Publish.NotPublished";

    public async Task<Result<ExamStatus>> PublishAsync(long examId, CancellationToken cancellationToken)
    {
        var exam = await FindAsync(examId, cancellationToken).ConfigureAwait(false);
        if (exam is null)
        {
            return Result.Fail<ExamStatus>(Problem.NotFound);
        }

        var problems = PublishValidator.Check(await FactsAsync(exam, cancellationToken).ConfigureAwait(false));
        if (problems.Count > 0)
        {
            return Result.Fail<ExamStatus>(problems);
        }

        exam.Status = ExamStatus.Published;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(exam.Status);
    }

    public async Task<Result<ExamStatus>> UnpublishAsync(long examId, CancellationToken cancellationToken)
    {
        var exam = await FindAsync(examId, cancellationToken).ConfigureAwait(false);
        if (exam is null)
        {
            return Result.Fail<ExamStatus>(Problem.NotFound);
        }

        if (exam.Status != ExamStatus.Published)
        {
            return Result.Fail<ExamStatus>(new Problem(NotPublished));
        }

        exam.Status = ExamStatus.Unpublished;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(exam.Status);
    }

    /// <summary>The publish problems for an exam as it is now (shown on the exam page before publishing).</summary>
    public async Task<IReadOnlyList<Problem>?> CheckAsync(long examId, CancellationToken cancellationToken)
    {
        var exam = await FindAsync(examId, cancellationToken).ConfigureAwait(false);
        return exam is null ? null : PublishValidator.Check(await FactsAsync(exam, cancellationToken).ConfigureAwait(false));
    }

    private Task<Exam?> FindAsync(long examId, CancellationToken cancellationToken) =>
        db.Exams.SingleOrDefaultAsync(e => e.Id == examId && e.OwnerKind == OwnerKind.App, cancellationToken);

    private async Task<PublishFacts> FactsAsync(Exam exam, CancellationToken cancellationToken)
    {
        var counts = await db.ExamQuestions
            .Where(eq => eq.ExamId == exam.Id && eq.Question!.Status == QuestionStatus.Active)
            .GroupBy(_ => 1)
            .Select(g => new { Active = g.Count(), Trial = g.Count(eq => eq.IsTrial) })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return new PublishFacts(exam.Status, exam.IsFree, exam.PriceIdr, exam.AccessDays, counts?.Active ?? 0, counts?.Trial ?? 0);
    }
}
