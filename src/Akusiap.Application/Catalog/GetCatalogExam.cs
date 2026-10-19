using Akusiap.Application.Abstractions;
using Akusiap.Application.Access;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Catalog;

/// <summary>A topic of the exam with its number of active questions.</summary>
public sealed record CatalogTopic(string Name, int QuestionCount);

public sealed record CatalogExamDetail(
    long Id,
    string Title,
    string? Description,
    string? SubjectName,
    ExamStatus Status,
    bool IsFree,
    long? PriceIdr,
    int? AccessDays,
    int ActiveQuestionCount,
    int TrialQuestionCount,
    IReadOnlyList<CatalogTopic> Topics,
    AccessDecision Decision)
{
    public IReadOnlyList<ExamCta> Ctas => ExamCtas.For(Decision);

    /// <summary>The viewer can use every question of a paid exam (purchase, subscription, admin).</summary>
    public bool HasFullAccess => !IsFree && Decision.Level is AccessLevel.Full or AccessLevel.Admin or AccessLevel.Owner;
}

/// <summary>
/// One app exam for its page at <c>/ujian/{id}</c> (CAT-02, stories G-01, G-04, G-06), with the current viewer's access
/// decision. Returns null when the page must answer 404: the exam doesn't exist, isn't an app exam, or is unpublished or
/// taken down for this viewer (admins still see drafts).
/// </summary>
public sealed class GetCatalogExam(IAppDbContext db, IEntitlementService entitlements)
{
    public async Task<CatalogExamDetail?> HandleAsync(long examId, CancellationToken cancellationToken)
    {
        var row = await db.Exams
            .Where(e => e.Id == examId && e.OwnerKind == OwnerKind.App)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Description,
                SubjectName = e.Subject == null ? null : e.Subject.Name,
                e.IsFree,
                e.PriceIdr,
                e.AccessDays,
                e.OwnerUserId,
                e.OwnerKind,
                e.Status,
                e.IsShared,
                e.ShareToken,
                ActiveQuestionCount = e.Questions.Count(eq => eq.Question!.Status == QuestionStatus.Active),
                TrialQuestionCount = e.Questions.Count(eq => eq.IsTrial && eq.Question!.Status == QuestionStatus.Active),
            })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            return null;
        }

        var facts = new ExamAccessFacts(row.Id, row.OwnerUserId, row.OwnerKind, row.Status, row.IsFree, row.IsShared, row.ShareToken, row.TrialQuestionCount > 0);
        var decision = await entitlements.DecideAsync(facts, shareToken: null, cancellationToken).ConfigureAwait(false);
        if (!ExamCtas.IsVisible(decision))
        {
            return null;
        }

        var topics = await db.ExamQuestions
            .Where(eq => eq.ExamId == examId && eq.Question!.Status == QuestionStatus.Active)
            .GroupBy(eq => eq.Question!.Topic!.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new CatalogExamDetail(
            row.Id,
            row.Title,
            row.Description,
            row.SubjectName,
            row.Status,
            row.IsFree,
            row.PriceIdr,
            row.AccessDays,
            row.ActiveQuestionCount,
            row.TrialQuestionCount,
            [.. topics.Select(t => new CatalogTopic(t.Name, t.Count))],
            decision);
    }
}
