using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Questions;

/// <summary>
/// Saves an app question and its options (ADM-01, ADM-03, ADM-31). Editing keeps the question id and the ids of options
/// that are kept, so past attempts stay intact; an option that past answers point to can't be removed.
/// </summary>
public sealed class SaveQuestion(IAppDbContext db, IClock clock)
{
    /// <summary>Arg: the option text.</summary>
    public const string OptionInUse = "Admin.Questions.OptionInUse";

    public Task<Result<long>> HandleAsync(SaveQuestionRequest request, CancellationToken cancellationToken) =>
        SaveAsync(request, ownerUserId: null, cancellationToken);

    /// <summary>Creates or edits a learner's own question (OWN-03). The caller checks the allowance first.</summary>
    public Task<Result<long>> SaveOwnAsync(SaveQuestionRequest request, Guid ownerUserId, CancellationToken cancellationToken) =>
        SaveAsync(request, ownerUserId, cancellationToken);

    private async Task<Result<long>> SaveAsync(SaveQuestionRequest request, Guid? ownerUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = QuestionValidator.Validate(request);
        if (problems.Count > 0)
        {
            return Result.Fail<long>(problems);
        }

        if (!await db.Topics.AnyAsync(t => t.Id == request.TopicId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Fail<long>(new Problem(QuestionValidator.TopicRequired, QuestionValidator.TopicField));
        }

        var now = clock.UtcNow;
        Question question;
        if (request.Id is { } id)
        {
            var existing = await db.Questions
                .Include(q => q.Options)
                .SingleOrDefaultAsync(q => q.Id == id && q.OwnerUserId == ownerUserId, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return Result.Fail<long>(Problem.NotFound);
            }

            question = existing;
        }
        else
        {
            question = new Question { CreatedAt = now, OwnerUserId = ownerUserId };
            db.Questions.Add(question);
        }

        var optionProblem = await SyncOptionsAsync(question, request.Options, cancellationToken).ConfigureAwait(false);
        if (optionProblem is not null)
        {
            return Result.Fail<long>(optionProblem);
        }

        question.TopicId = request.TopicId;
        question.QuestionType = request.Type!.Value;
        question.Text = TextRules.Clean(request.Text)!;
        question.Explanation = TextRules.Clean(request.Explanation)!;
        question.KeyFact = TextRules.Clean(request.KeyFact);
        question.Hint = TextRules.Clean(request.Hint);
        question.Difficulty = request.Difficulty;
        question.SourceNote = TextRules.Clean(request.SourceNote);
        question.Status = request.Status!.Value;
        question.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(question.Id);
    }

    private async Task<Problem?> SyncOptionsAsync(Question question, IReadOnlyList<OptionRequest> requested, CancellationToken cancellationToken)
    {
        var keptIds = requested.Where(o => o.Id is not null).Select(o => o.Id!.Value).ToHashSet();
        var removed = question.Options.Where(o => !keptIds.Contains(o.Id)).ToList();
        var removedIds = removed.Select(o => o.Id).Where(id => id != 0).ToList();
        if (removedIds.Count > 0)
        {
            var usedId = await db.AttemptItemOptions
                .Where(a => removedIds.Contains(a.QuestionOptionId))
                .Select(a => (long?)a.QuestionOptionId)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            if (usedId is { } used)
            {
                return new Problem(OptionInUse, QuestionValidator.OptionsField, removed.Single(o => o.Id == used).Text);
            }
        }

        foreach (var option in removed)
        {
            question.Options.Remove(option);
        }

        for (var i = 0; i < requested.Count; i++)
        {
            var input = requested[i];
            var option = input.Id is { } optionId ? question.Options.SingleOrDefault(o => o.Id == optionId) : null;
            if (option is null)
            {
                option = new QuestionOption();
                question.Options.Add(option);
            }

            option.Text = TextRules.Clean(input.Text)!;
            option.IsCorrect = input.IsCorrect;
            option.SortOrder = i + 1;
        }

        return null;
    }
}
