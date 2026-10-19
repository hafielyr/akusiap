using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Exams;

/// <summary>
/// Changes to an app exam's question list (ADM-T4): add, remove, move and toggle trial. Each returns the refreshed
/// <see cref="ExamQuestionsView"/> so the page can swap in the new list and counters. Positions are kept as 1..n.
/// </summary>
public sealed class ChangeExamQuestions(IAppDbContext db, GetExamQuestions getExamQuestions)
{
    public const string AlreadyAdded = "Admin.ExamQuestions.AlreadyAdded";
    public const string NotAddable = "Admin.ExamQuestions.NotAddable";
    public const string NotInExam = "Admin.ExamQuestions.NotInExam";

    public async Task<Result<ExamQuestionsView>> AddAsync(long examId, long questionId, CancellationToken cancellationToken)
    {
        if (!await ExamExistsAsync(examId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<ExamQuestionsView>(Problem.NotFound);
        }

        var addable = await db.Questions
            .AnyAsync(q => q.Id == questionId && q.OwnerUserId == null && q.Status == QuestionStatus.Active, cancellationToken)
            .ConfigureAwait(false);
        if (!addable)
        {
            return Result.Fail<ExamQuestionsView>(new Problem(NotAddable));
        }

        if (await db.ExamQuestions.AnyAsync(eq => eq.ExamId == examId && eq.QuestionId == questionId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<ExamQuestionsView>(new Problem(AlreadyAdded));
        }

        var last = await db.ExamQuestions
            .Where(eq => eq.ExamId == examId)
            .MaxAsync(eq => (int?)eq.SortOrder, cancellationToken)
            .ConfigureAwait(false) ?? 0;
        db.ExamQuestions.Add(new ExamQuestion { ExamId = examId, QuestionId = questionId, SortOrder = last + 1 });
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Added at the same moment (the (exam_id, question_id) primary key).
            return Result.Fail<ExamQuestionsView>(new Problem(AlreadyAdded));
        }

        return await ViewAsync(examId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ExamQuestionsView>> RemoveAsync(long examId, long questionId, CancellationToken cancellationToken)
    {
        var links = await LoadAsync(examId, cancellationToken).ConfigureAwait(false);
        if (links is null)
        {
            return Result.Fail<ExamQuestionsView>(Problem.NotFound);
        }

        var link = links.SingleOrDefault(l => l.QuestionId == questionId);
        if (link is null)
        {
            return Result.Fail<ExamQuestionsView>(new Problem(NotInExam));
        }

        db.ExamQuestions.Remove(link);
        links.Remove(link);
        Renumber(links);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ViewAsync(examId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Moves a question one place up (<paramref name="step"/> = -1) or down (+1); no-op at either end.</summary>
    public async Task<Result<ExamQuestionsView>> MoveAsync(long examId, long questionId, int step, CancellationToken cancellationToken)
    {
        var links = await LoadAsync(examId, cancellationToken).ConfigureAwait(false);
        if (links is null)
        {
            return Result.Fail<ExamQuestionsView>(Problem.NotFound);
        }

        var index = links.FindIndex(l => l.QuestionId == questionId);
        if (index < 0)
        {
            return Result.Fail<ExamQuestionsView>(new Problem(NotInExam));
        }

        var target = index + Math.Sign(step);
        if (target >= 0 && target < links.Count)
        {
            (links[index], links[target]) = (links[target], links[index]);
        }

        Renumber(links);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ViewAsync(examId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<ExamQuestionsView>> SetTrialAsync(long examId, long questionId, bool isTrial, CancellationToken cancellationToken)
    {
        var links = await LoadAsync(examId, cancellationToken).ConfigureAwait(false);
        if (links is null)
        {
            return Result.Fail<ExamQuestionsView>(Problem.NotFound);
        }

        var link = links.SingleOrDefault(l => l.QuestionId == questionId);
        if (link is null)
        {
            return Result.Fail<ExamQuestionsView>(new Problem(NotInExam));
        }

        link.IsTrial = isTrial;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ViewAsync(examId, cancellationToken).ConfigureAwait(false);
    }

    private Task<bool> ExamExistsAsync(long examId, CancellationToken cancellationToken) =>
        db.Exams.AnyAsync(e => e.Id == examId && e.OwnerKind == OwnerKind.App, cancellationToken);

    /// <summary>The exam's links in their current order, or null when the exam doesn't exist.</summary>
    private async Task<List<ExamQuestion>?> LoadAsync(long examId, CancellationToken cancellationToken)
    {
        if (!await ExamExistsAsync(examId, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return await db.ExamQuestions
            .Where(eq => eq.ExamId == examId)
            .OrderBy(eq => eq.SortOrder)
            .ThenBy(eq => eq.QuestionId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void Renumber(List<ExamQuestion> links)
    {
        for (var i = 0; i < links.Count; i++)
        {
            links[i].SortOrder = i + 1;
        }
    }

    private async Task<Result<ExamQuestionsView>> ViewAsync(long examId, CancellationToken cancellationToken)
    {
        var view = await getExamQuestions.HandleAsync(examId, cancellationToken).ConfigureAwait(false);
        return view is null ? Result.Fail<ExamQuestionsView>(Problem.NotFound) : Result.Ok(view);
    }
}
