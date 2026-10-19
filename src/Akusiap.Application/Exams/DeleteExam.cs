using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Exams;

/// <summary>
/// Deletes an app exam that was never used: not published, no attempts and no purchases. Its question links go with it;
/// the questions stay in the bank. Used exams are unpublished instead (ADM-T6).
/// </summary>
public sealed class DeleteExam(IAppDbContext db)
{
    public const string DeletePublished = "Admin.Exams.DeletePublished";

    /// <summary>Args: attempt count, purchase count.</summary>
    public const string InUse = "Admin.Exams.InUse";

    public async Task<Result<bool>> HandleAsync(long id, CancellationToken cancellationToken)
    {
        var exam = await db.Exams
            .SingleOrDefaultAsync(e => e.Id == id && e.OwnerKind == OwnerKind.App, cancellationToken)
            .ConfigureAwait(false);
        if (exam is null)
        {
            return Result.Fail<bool>(Problem.NotFound);
        }

        if (exam.Status == ExamStatus.Published)
        {
            return Result.Fail<bool>(new Problem(DeletePublished));
        }

        var attempts = await db.Attempts.CountAsync(a => a.ExamId == id, cancellationToken).ConfigureAwait(false);
        var purchases = await db.ExamPurchases.CountAsync(p => p.ExamId == id, cancellationToken).ConfigureAwait(false);
        if (attempts > 0 || purchases > 0)
        {
            return Result.Fail<bool>(new Problem(InUse, null, attempts, purchases));
        }

        db.Exams.Remove(exam);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(true);
    }
}
