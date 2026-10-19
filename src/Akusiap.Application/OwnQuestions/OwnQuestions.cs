using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Application.Questions;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.OwnQuestions;

public sealed record OwnQuestionRow(long Id, string Text, string TopicName, QuestionType Type, QuestionStatus Status, DateTime UpdatedAt);

/// <summary>
/// A learner's own questions (OWN-03, stories C-01, C-05, D-71). Creating needs room in the allowance; editing needs
/// the allowance to cover current usage (D-07, read-only otherwise). Deleting is refused when the question is in an
/// attempt or an exam; retire it instead. Own questions use any existing topic, or the shared "Umum / Umum" topic.
/// </summary>
public sealed class OwnQuestions(IAppDbContext db, SaveQuestion saveQuestion, GetQuestion getQuestion, GetAllowance getAllowance, ICurrentUser currentUser)
{
    public const string LimitReached = "Own.Questions.LimitReached";
    public const string ReadOnly = "Own.Questions.ReadOnly";
    public const string InUse = "Own.Questions.InUse";
    public const string GeneralName = "Umum";

    public async Task<IReadOnlyList<OwnQuestionRow>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        return await db.Questions
            .Where(q => q.OwnerUserId == userId)
            .OrderByDescending(q => q.UpdatedAt)
            .ThenByDescending(q => q.Id)
            .Select(q => new OwnQuestionRow(q.Id, q.Text, q.Topic!.Name, q.QuestionType, q.Status, q.UpdatedAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<QuestionDetail?> GetAsync(long id, CancellationToken cancellationToken) =>
        getQuestion.ForOwnerAsync(id, UserId(), cancellationToken);

    public async Task<Result<long>> SaveAsync(SaveQuestionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var allowance = await getAllowance.HandleAsync(cancellationToken).ConfigureAwait(false);
        if (request.Id is null && !allowance.CanCreateQuestion)
        {
            return Result.Fail<long>(new Problem(allowance.CanEdit ? LimitReached : ReadOnly, null, allowance.MaxQuestions ?? 0));
        }

        if (request.Id is not null && !allowance.CanEdit)
        {
            return Result.Fail<long>(new Problem(ReadOnly));
        }

        return await saveQuestion.SaveOwnAsync(request, UserId(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<bool>> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var userId = UserId();
        var question = await db.Questions.Include(q => q.Options).SingleOrDefaultAsync(q => q.Id == id && q.OwnerUserId == userId, cancellationToken).ConfigureAwait(false);
        if (question is null)
        {
            return Result.Fail<bool>(Problem.NotFound);
        }

        var used = await db.AttemptItems.AnyAsync(i => i.QuestionId == id, cancellationToken).ConfigureAwait(false)
            || await db.ExamQuestions.AnyAsync(e => e.QuestionId == id, cancellationToken).ConfigureAwait(false);
        if (used)
        {
            return Result.Fail<bool>(new Problem(InUse));
        }

        db.Questions.Remove(question);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(true);
    }

    /// <summary>The shared "Umum" topic (subject "Umum") for own questions without a better topic; created when missing.</summary>
    public async Task<long> GeneralTopicIdAsync(CancellationToken cancellationToken)
    {
        var id = await db.Topics.Where(t => t.Name == GeneralName && t.Subject!.Name == GeneralName).Select(t => (long?)t.Id)
            .FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (id is { } existing)
        {
            return existing;
        }

        var subject = await db.Subjects.FirstOrDefaultAsync(s => s.Name == GeneralName, cancellationToken).ConfigureAwait(false)
            ?? db.Subjects.Add(new Subject { Name = GeneralName }).Entity;
        var topic = new Topic { Subject = subject, Name = GeneralName };
        db.Topics.Add(topic);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return topic.Id;
    }

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("Own questions need a signed-in user.");
}
