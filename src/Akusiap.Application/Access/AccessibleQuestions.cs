using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Application.Access;

/// <summary>
/// The questions a signed-in learner can fully use right now (DRL-01): their own Active questions, plus the Active
/// questions of every exam where the entitlement is Full, Owner or Admin (purchases, subscriptions, free exams for
/// registered users, their own exams and open class assignments). Trial access doesn't count.
/// </summary>
public sealed class AccessibleQuestions(IAppDbContext db, IEntitlementService entitlements, ExamQuestionScope questionScope, ICurrentUser currentUser)
{
    public async Task<IReadOnlyList<long>> ForCurrentUserAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("Accessible questions need a signed-in user.");
        var viewer = await entitlements.GetViewerAsync(cancellationToken).ConfigureAwait(false);
        var assigned = viewer.ClassAssignments.Select(a => a.ExamId).ToList();
        var exams = await db.Exams.AsNoTracking()
            .Where(e => (e.OwnerKind == OwnerKind.App && e.Status == ExamStatus.Published) || e.OwnerUserId == userId || assigned.Contains(e.Id))
            .Select(e => new ExamAccessFacts(e.Id, e.OwnerUserId, e.OwnerKind, e.Status, e.IsFree, e.IsShared, e.ShareToken, e.Questions.Any(q => q.IsTrial)))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var ids = new HashSet<long>(await db.Questions
            .Where(q => q.OwnerUserId == userId && q.Status == QuestionStatus.Active)
            .Select(q => q.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false));
        foreach (var exam in exams)
        {
            var decision = await entitlements.DecideAsync(exam, shareToken: null, cancellationToken).ConfigureAwait(false);
            if (decision.CanStart && decision.Level is AccessLevel.Full or AccessLevel.Owner or AccessLevel.Admin)
            {
                ids.UnionWith(await questionScope.GetQuestionIdsAsync(exam.Id, QuestionScope.All, cancellationToken).ConfigureAwait(false));
            }
        }

        return [.. ids];
    }
}
