using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Exams;

/// <summary>
/// Saves an app exam (ADM-T3). A new exam always starts as Draft; publishing is a separate step (ADM-T6), so editing
/// never changes the status. A free exam stores no price or access days.
/// </summary>
public sealed class SaveExam(IAppDbContext db, IClock clock)
{
    public async Task<Result<long>> HandleAsync(SaveExamRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = ExamValidator.Validate(request);
        if (problems.Count > 0)
        {
            return Result.Fail<long>(problems);
        }

        if (request.SubjectId is { } subjectId
            && !await db.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<long>(new Problem(ExamValidator.SubjectMissing, ExamValidator.SubjectField));
        }

        if (!await db.RulePresets.AnyAsync(r => r.Id == request.RulePresetId && r.OwnerUserId == null, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<long>(new Problem(ExamValidator.RulePresetRequired, ExamValidator.RulePresetField));
        }

        Exam exam;
        if (request.Id is { } id)
        {
            var existing = await db.Exams
                .SingleOrDefaultAsync(e => e.Id == id && e.OwnerKind == OwnerKind.App, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return Result.Fail<long>(Problem.NotFound);
            }

            exam = existing;
        }
        else
        {
            exam = new Exam { OwnerKind = OwnerKind.App, Status = ExamStatus.Draft, CreatedAt = clock.UtcNow };
            db.Exams.Add(exam);
        }

        exam.Title = TextRules.Clean(request.Title)!;
        exam.Description = TextRules.Clean(request.Description);
        exam.SubjectId = request.SubjectId;
        exam.IsFree = request.IsFree;
        exam.PriceIdr = request.IsFree ? null : request.PriceIdr;
        exam.AccessDays = request.IsFree ? null : request.AccessDays;
        exam.OwnQuestionAllowance = request.OwnQuestionAllowance;
        exam.RulePresetId = request.RulePresetId;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(exam.Id);
    }
}
