using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Application.Common;
using Akusiap.Application.Sessions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.OwnExams;

public sealed record MixTopic(long TopicId, string Name, int QuestionCount);

public sealed record MixExam(long ExamId, string Title, IReadOnlyList<MixTopic> Topics);

/// <summary>What the learner can mix: their own active questions and the exams they have full access to.</summary>
public sealed record MixOptions(int OwnQuestionCount, IReadOnlyList<MixExam> Exams);

/// <param name="TopicKeys">Single topics of an exam, as <c>examId:topicId</c>.</param>
public sealed record StartMixRequest(bool IncludeOwn, IReadOnlyList<long> ExamIds, IReadOnlyList<string> TopicKeys, int QuestionCount);

/// <summary>
/// A private mixed practice set (OWN-06, story C-08, D-84): own questions plus questions from exams the learner has full
/// access to (Full or Admin; trial access doesn't count), whole exams or single topics. Every app question is checked
/// against the entitlement again when the set starts, so anything without full access is dropped, whatever was posted.
/// The set is a Practice attempt with Source CustomMix and no exam, so it can never be shared.
/// </summary>
public sealed class MixedPractice(IAppDbContext db, IEntitlementService entitlements, ExamQuestionScope questionScope, StartAttempt startAttempt, ICurrentUser currentUser)
{
    public const int DefaultCount = 20;
    public const int MaxCount = 100;
    public const string NothingSelected = "Mix.NothingSelected";
    public const string CountField = "QuestionCount";

    public async Task<MixOptions> OptionsAsync(CancellationToken cancellationToken)
    {
        var userId = UserId();
        var own = await db.Questions.CountAsync(q => q.OwnerUserId == userId && q.Status == QuestionStatus.Active, cancellationToken).ConfigureAwait(false);
        var exams = new List<MixExam>();
        foreach (var exam in await FullAccessExamsAsync(cancellationToken).ConfigureAwait(false))
        {
            var topics = await db.ExamQuestions.AsNoTracking()
                .Where(eq => eq.ExamId == exam.Id && eq.Question!.Status == QuestionStatus.Active)
                .GroupBy(eq => new { eq.Question!.TopicId, eq.Question.Topic!.Name })
                .Select(g => new { g.Key.TopicId, g.Key.Name, Count = g.Count() })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            exams.Add(new MixExam(exam.Id, exam.Title, [.. topics.OrderBy(t => t.Name, StringComparer.CurrentCulture).Select(t => new MixTopic(t.TopicId, t.Name, t.Count))]));
        }

        return new MixOptions(own, exams);
    }

    public async Task<Result<StartedAttempt>> StartAsync(StartMixRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.QuestionCount is < 1 or > MaxCount)
        {
            return Result.Fail<StartedAttempt>(new Problem("Validation.Range", CountField, "Mix.Count", 1, MaxCount));
        }

        var topicPicks = (request.TopicKeys ?? [])
            .Select(k => k.Split(':'))
            .Where(p => p.Length == 2 && long.TryParse(p[0], out _) && long.TryParse(p[1], out _))
            .Select(p => (ExamId: long.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture), TopicId: long.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();
        var wholeExams = (request.ExamIds ?? []).Distinct().ToList();
        if (!request.IncludeOwn && wholeExams.Count == 0 && topicPicks.Count == 0)
        {
            return Result.Fail<StartedAttempt>(new Problem(NothingSelected));
        }

        var userId = UserId();
        var ids = new List<long>();
        if (request.IncludeOwn)
        {
            ids.AddRange(await db.Questions
                .Where(q => q.OwnerUserId == userId && q.Status == QuestionStatus.Active)
                .OrderBy(q => q.Id)
                .Select(q => q.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        var allowedExams = (await FullAccessExamsAsync(cancellationToken).ConfigureAwait(false)).Select(e => e.Id).ToHashSet();
        foreach (var examId in wholeExams.Concat(topicPicks.Select(p => p.ExamId)).Distinct().Where(allowedExams.Contains))
        {
            var examQuestions = await questionScope.GetQuestionIdsAsync(examId, QuestionScope.All, cancellationToken).ConfigureAwait(false);
            if (wholeExams.Contains(examId))
            {
                ids.AddRange(examQuestions);
                continue;
            }

            var topics = topicPicks.Where(p => p.ExamId == examId).Select(p => p.TopicId).ToList();
            ids.AddRange(await db.Questions
                .Where(q => examQuestions.Contains(q.Id) && topics.Contains(q.TopicId))
                .Select(q => q.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false));
        }

        return await startAttempt.StartCustomMixAsync([.. ids.Distinct()], request.QuestionCount, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Published app exams where the viewer's decision is Full or Admin.</summary>
    private async Task<List<(long Id, string Title)>> FullAccessExamsAsync(CancellationToken cancellationToken)
    {
        var exams = await db.Exams.AsNoTracking()
            .Where(e => e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published)
            .OrderBy(e => e.Title)
            .Select(e => new { Facts = new ExamAccessFacts(e.Id, e.OwnerUserId, e.OwnerKind, e.Status, e.IsFree, e.IsShared, e.ShareToken, e.Questions.Any(q => q.IsTrial)), e.Title })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var result = new List<(long, string)>();
        foreach (var exam in exams)
        {
            var decision = await entitlements.DecideAsync(exam.Facts, shareToken: null, cancellationToken).ConfigureAwait(false);
            if (decision.CanStart && decision.Level is AccessLevel.Full or AccessLevel.Admin)
            {
                result.Add((exam.Facts.Id, exam.Title));
            }
        }

        return result;
    }

    private Guid UserId() => currentUser.UserId ?? throw new InvalidOperationException("Mixed practice needs a signed-in user.");
}
