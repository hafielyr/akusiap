using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Application.Exams;
using Akusiap.Application.RulePresets;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.OwnExams;

public sealed record OwnExamRow(long Id, string Title, ExamStatus Status, int QuestionCount);

/// <summary>Create (Id null) or edit a learner's own exam. Null limits mean untimed, all questions and no pass mark.</summary>
public sealed record SaveOwnExamRequest(
    long? Id,
    string? Title,
    string? Description,
    bool Published,
    IReadOnlyList<long> QuestionIds,
    int? TimeLimitMinutes,
    int? QuestionCount,
    bool HintsEnabled,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    int? PassMarkPercent,
    ShowAnswers? ShowAnswers,
    bool CatMode = false);

public sealed record OwnExamDetail(
    long Id,
    string Title,
    string? Description,
    ExamStatus Status,
    IReadOnlyList<long> QuestionIds,
    int? TimeLimitMinutes,
    int? QuestionCount,
    bool HintsEnabled,
    bool ShuffleQuestions,
    bool ShuffleOptions,
    int? PassMarkPercent,
    ShowAnswers ShowAnswers,
    bool CatMode = false);

/// <summary>A question the learner can put in an own exam: their own, not retired.</summary>
public sealed record OwnExamQuestionOption(long Id, string Text, string TopicName);

/// <summary>
/// A learner's own exams (OWN-04, story C-02, D-78): OwnerKind Learner, free, built from the learner's own (not retired)
/// questions only, with a personal rule preset (OwnerUserId) kept in step with the exam. Draft or Published, where
/// Published only means "ready to share"; the owner can practise it at any status (entitlement rule 2). Creating needs
/// room in the allowance (the free plan's 2nd exam is refused); editing needs the allowance to cover current usage (D-07).
/// </summary>
public sealed class OwnExams(IAppDbContext db, GetAllowance getAllowance, ICurrentUser currentUser, IClock clock)
{
    public const string LimitReached = "Own.Exams.LimitReached";
    public const string ReadOnly = "Own.Exams.ReadOnly";
    public const string NoQuestions = "Own.Exams.NoQuestions";
    public const string QuestionsField = "QuestionIds";

    public async Task<IReadOnlyList<OwnExamRow>> ListAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        return await db.Exams
            .Where(e => e.OwnerUserId == userId && e.OwnerKind == OwnerKind.Learner)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Select(e => new OwnExamRow(e.Id, e.Title, e.Status, e.Questions.Count))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OwnExamDetail?> GetAsync(long id, CancellationToken cancellationToken)
    {
        var userId = UserId();
        return await db.Exams
            .Where(e => e.Id == id && e.OwnerUserId == userId && e.OwnerKind == OwnerKind.Learner)
            .Select(e => new OwnExamDetail(
                e.Id,
                e.Title,
                e.Description,
                e.Status,
                e.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId).ToList(),
                e.RulePreset!.TimeLimitMinutes,
                e.RulePreset.QuestionCount,
                e.RulePreset.HintsEnabled,
                e.RulePreset.ShuffleQuestions,
                e.RulePreset.ShuffleOptions,
                e.RulePreset.PassMarkPercent,
                e.RulePreset.ShowAnswers,
                e.RulePreset.CatMode))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The questions that can go in an own exam, newest first.</summary>
    public async Task<IReadOnlyList<OwnExamQuestionOption>> QuestionOptionsAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        return await db.Questions
            .Where(q => q.OwnerUserId == userId && q.Status != QuestionStatus.Retired)
            .OrderByDescending(q => q.UpdatedAt)
            .ThenByDescending(q => q.Id)
            .Select(q => new OwnExamQuestionOption(q.Id, q.Text, q.Topic!.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<long>> SaveAsync(SaveOwnExamRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var userId = UserId();
        var allowance = await getAllowance.HandleAsync(cancellationToken).ConfigureAwait(false);
        if (request.Id is null && !allowance.CanCreateExam)
        {
            return Result.Fail<long>(new Problem(allowance.CanEdit ? LimitReached : ReadOnly, null, allowance.MaxExams ?? 0));
        }

        if (request.Id is not null && !allowance.CanEdit)
        {
            return Result.Fail<long>(new Problem(ReadOnly));
        }

        var problems = new List<Problem>();
        TextRules.RequireName(problems, ExamValidator.TitleField, ExamValidator.TitleLabel, request.Title);
        if (TextRules.Clean(request.Description) is { Length: > ExamValidator.DescriptionMaxLength })
        {
            problems.Add(new Problem(TextRules.TooLong, ExamValidator.DescriptionField, ExamValidator.DescriptionLabel, ExamValidator.DescriptionMaxLength));
        }

        problems.AddRange(RulePresetValidator.Validate(new SaveRulePresetRequest(
            null, "-", request.TimeLimitMinutes, request.QuestionCount, request.HintsEnabled, request.ShuffleQuestions,
            request.ShuffleOptions, null, request.PassMarkPercent, request.ShowAnswers, false)));

        var questionIds = (request.QuestionIds ?? []).Distinct().ToList();
        var allowed = await db.Questions
            .Where(q => questionIds.Contains(q.Id) && q.OwnerUserId == userId && q.Status != QuestionStatus.Retired)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
        if (questionIds.Count == 0 || allowed != questionIds.Count)
        {
            problems.Add(new Problem(NoQuestions, QuestionsField));
        }

        if (problems.Count > 0)
        {
            return Result.Fail<long>(problems);
        }

        Exam exam;
        if (request.Id is { } id)
        {
            var existing = await db.Exams
                .Include(e => e.RulePreset)
                .Include(e => e.Questions)
                .SingleOrDefaultAsync(e => e.Id == id && e.OwnerUserId == userId && e.OwnerKind == OwnerKind.Learner, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
            {
                return Result.Fail<long>(Problem.NotFound);
            }

            exam = existing;
        }
        else
        {
            exam = new Exam
            {
                OwnerUserId = userId,
                OwnerKind = OwnerKind.Learner,
                IsFree = true,
                CreatedAt = clock.UtcNow,
                RulePreset = new RulePreset { OwnerUserId = userId },
            };
            db.Exams.Add(exam);
        }

        exam.Title = TextRules.Clean(request.Title)!;
        exam.Description = TextRules.Clean(request.Description);
        if (exam.Status != ExamStatus.TakenDown)
        {
            exam.Status = request.Published ? ExamStatus.Published : ExamStatus.Draft;
        }

        var preset = exam.RulePreset!;
        preset.Name = exam.Title;
        preset.TimeLimitMinutes = request.TimeLimitMinutes;
        preset.QuestionCount = request.QuestionCount;
        preset.HintsEnabled = request.HintsEnabled;
        preset.ShuffleQuestions = request.ShuffleQuestions;
        preset.ShuffleOptions = request.ShuffleOptions;
        preset.MaxAttempts = null;
        preset.PassMarkPercent = request.PassMarkPercent;
        preset.ShowAnswers = request.ShowAnswers!.Value;
        preset.CatMode = request.CatMode;

        foreach (var stale in exam.Questions.Where(q => !questionIds.Contains(q.QuestionId)).ToList())
        {
            exam.Questions.Remove(stale);
        }

        for (var i = 0; i < questionIds.Count; i++)
        {
            var questionId = questionIds[i];
            var link = exam.Questions.SingleOrDefault(q => q.QuestionId == questionId);
            if (link is null)
            {
                exam.Questions.Add(new ExamQuestion { QuestionId = questionId, SortOrder = i + 1 });
            }
            else
            {
                link.SortOrder = i + 1;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Ok(exam.Id);
    }

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("Own exams need a signed-in user.");
}
