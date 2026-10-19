using System.Security.Cryptography;
using System.Text;
using Akusiap.Domain.Content;

namespace Akusiap.Domain.Access;

/// <summary>
/// Every access decision for an exam (architecture.md, "Entitlement rules"). Rules are checked in order and the
/// first match wins. Pure: no I/O, the caller supplies the facts and the current UTC time.
/// </summary>
public static class EntitlementPolicy
{
    public static AccessDecision Evaluate(ViewerContext viewer, ExamAccessFacts exam, string? shareToken, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(exam);
        if (now.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Use a UTC time.", nameof(now));
        }

        // 1. Admin.
        if (viewer.IsAdmin)
        {
            return All(AccessLevel.Admin, AccessReason.Admin);
        }

        // 2. Owner, at any status; a taken-down exam can be viewed but not started.
        if (viewer.UserId is { } userId && exam.OwnerUserId == userId)
        {
            return All(AccessLevel.Owner, exam.Status == ExamStatus.TakenDown ? AccessReason.OwnerTakenDown : AccessReason.Owner);
        }

        // 3. Taken down.
        if (exam.Status == ExamStatus.TakenDown)
        {
            return NoAccess(AccessReason.TakenDown);
        }

        // 4. Shared link to a learner's exam.
        if (exam.IsShared && exam.OwnerKind == OwnerKind.Learner && TokenMatches(shareToken, exam.ShareToken))
        {
            return All(AccessLevel.Shared, AccessReason.SharedLink) with { RequiresGuestName = !viewer.IsRegistered };
        }

        // 5. Not in the catalog. A learner's exam never is: Published only means "ready to share" (D-79).
        if (exam.Status != ExamStatus.Published || exam.OwnerKind == OwnerKind.Learner)
        {
            return NoAccess(AccessReason.NotPublished);
        }

        // 6. Free exams: registered users only.
        if (exam.IsFree)
        {
            return viewer.IsRegistered ? All(AccessLevel.Full, AccessReason.FreeExam) : NoAccess(AccessReason.RegisterToUnlock);
        }

        // 7. An active purchase of this exam that has not expired.
        if (viewer.Purchases.Any(p => p.ExamId == exam.Id && p.Status == ExamPurchaseStatus.Active && p.AccessExpiresAt > now))
        {
            return All(AccessLevel.Full, AccessReason.Purchased);
        }

        // (Release 3: teacher class assignments go here, between 7 and 8.)

        // 8. An active subscription to a plan with all exams.
        if (viewer.Subscriptions.Any(s => s.Status == SubscriptionStatus.Active && s.CurrentPeriodEnd > now && s.IncludesAllExams))
        {
            return All(AccessLevel.Full, AccessReason.Subscription);
        }

        // 9. The trial set of a paid app exam, for guests and registered users alike.
        if (exam.OwnerKind == OwnerKind.App && exam.HasTrialQuestions)
        {
            return new AccessDecision(AccessLevel.Trial, AccessReason.TrialAvailable, QuestionScope.TrialOnly);
        }

        // 10. Otherwise.
        return NoAccess(AccessReason.NoAccess);
    }

    private static AccessDecision All(AccessLevel level, AccessReason reason) => new(level, reason, QuestionScope.All);

    private static AccessDecision NoAccess(AccessReason reason) => new(AccessLevel.None, reason, QuestionScope.None);

    /// <summary>Exact, case-sensitive match in constant time, so response timing reveals nothing about the token.</summary>
    private static bool TokenMatches(string? supplied, string? expected) =>
        !string.IsNullOrEmpty(supplied)
        && !string.IsNullOrEmpty(expected)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
}
