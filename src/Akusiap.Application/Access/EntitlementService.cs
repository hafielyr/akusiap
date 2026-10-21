using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Access;

/// <summary>
/// Loads the facts <see cref="EntitlementPolicy"/> needs and evaluates it (ENT-02). Registered per request (scoped):
/// the viewer's purchases and subscriptions are read once, in a single query, and reused for every exam asked about.
/// </summary>
public sealed class EntitlementService(IAppDbContext db, ICurrentUser currentUser, IClock clock) : IEntitlementService
{
    /// <summary>EF query tag on the viewer query, so tests and logs can see how often it runs.</summary>
    public const string ViewerQueryTag = "entitlement:viewer-context";

    private Task<ViewerContext>? _viewer;

    public Task<ViewerContext> GetViewerAsync(CancellationToken cancellationToken) =>
        _viewer ??= LoadViewerAsync(cancellationToken);

    public async Task<AccessDecision> DecideAsync(ExamAccessFacts exam, string? shareToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exam);
        var viewer = await GetViewerAsync(cancellationToken).ConfigureAwait(false);
        return EntitlementPolicy.Evaluate(viewer, exam, shareToken, clock.UtcNow);
    }

    public async Task<EntitlementResult?> GetAsync(long examId, string? shareToken, CancellationToken cancellationToken)
    {
        var exam = await db.Exams
            .Where(e => e.Id == examId)
            .Select(e => new ExamAccessFacts(
                e.Id,
                e.OwnerUserId,
                e.OwnerKind,
                e.Status,
                e.IsFree,
                e.IsShared,
                e.ShareToken,
                e.Questions.Any(eq => eq.IsTrial && eq.Question!.Status == QuestionStatus.Active)))
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (exam is null)
        {
            return null;
        }

        var viewer = await GetViewerAsync(cancellationToken).ConfigureAwait(false);
        return new EntitlementResult(exam, EntitlementPolicy.Evaluate(viewer, exam, shareToken, clock.UtcNow));
    }

    private async Task<ViewerContext> LoadViewerAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return ViewerContext.Guest;
        }

        // Only rows that can still grant access: Active and ending after now, plus class assignments (GAM-01) in the
        // viewer's current classes that have not closed. One UNION ALL query.
        var now = clock.UtcNow;
        var rows = await db.ExamPurchases
            .Where(p => p.UserId == userId && p.Status == ExamPurchaseStatus.Active && p.AccessExpiresAt > now)
            .Select(p => new { Kind = 0, ExamId = (long?)p.ExamId, Start = (DateTime?)null, End = p.AccessExpiresAt, IncludesAllExams = false })
            .Concat(db.Subscriptions
                .Where(s => s.UserId == userId && s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd > now)
                .Select(s => new { Kind = 1, ExamId = (long?)null, Start = (DateTime?)null, End = s.CurrentPeriodEnd, IncludesAllExams = s.Plan!.IncludesAllExams }))
            .Concat(db.ClassAssignments
                .Where(a => a.ClosesAt > now && a.Classroom!.Members.Any(m => m.UserId == userId && m.RemovedAt == null))
                .Select(a => new { Kind = 2, ExamId = (long?)a.ExamId, Start = (DateTime?)a.OpensAt, End = (DateTime?)a.ClosesAt, IncludesAllExams = false }))
            .TagWith(ViewerQueryTag)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var purchases = rows
            .Where(r => r.Kind == 0)
            .Select(r => new PurchaseFact(r.ExamId!.Value, ExamPurchaseStatus.Active, r.End))
            .ToList();
        var subscriptions = rows
            .Where(r => r.Kind == 1)
            .Select(r => new SubscriptionFact(SubscriptionStatus.Active, r.End, r.IncludesAllExams))
            .ToList();
        var assignments = rows
            .Where(r => r.Kind == 2)
            .Select(r => new ClassAssignmentFact(r.ExamId!.Value, r.Start!.Value, r.End!.Value))
            .ToList();
        return new ViewerContext(userId, currentUser.IsAdmin, purchases, subscriptions) { ClassAssignments = assignments };
    }
}
