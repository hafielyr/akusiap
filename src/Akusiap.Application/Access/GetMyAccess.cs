using Akusiap.Application.Abstractions;

namespace Akusiap.Application.Access;

/// <summary>One exam purchase or subscription on the learner's access page.</summary>
/// <param name="ExamId">The exam of a purchase; null for subscriptions.</param>
public sealed record MyAccessItem(
    AccessKind Kind,
    long Id,
    string Title,
    AccessState State,
    DateTime? EndsAt,
    int? DaysLeft,
    long? ExamId,
    bool IncludesAllExams);

public sealed record MyAccess(IReadOnlyList<MyAccessItem> Active, IReadOnlyList<MyAccessItem> Ended);

/// <summary>
/// The signed-in learner's own access (ENT-04, story P-06): active items first, soonest end first; then ended and
/// refunded items, latest end first. Unpaid and cancelled checkouts are left out (PAY-05 shows pending payments).
/// </summary>
public sealed class GetMyAccess(GetUserAccess userAccess, ICurrentUser currentUser)
{
    public async Task<MyAccess> HandleAsync(CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("My access needs a signed-in user.");
        var access = await userAccess.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (access is null)
        {
            return new MyAccess([], []);
        }

        var items = access.Purchases
            .Select(p => new MyAccessItem(AccessKind.Purchase, p.Id, p.ExamTitle, p.State, p.EndsAt, p.DaysLeft, p.ExamId, false))
            .Concat(access.Subscriptions
                .Select(s => new MyAccessItem(AccessKind.Subscription, s.Id, s.PlanName, s.State, s.EndsAt, s.DaysLeft, null, s.IncludesAllExams)))
            .ToList();

        return new MyAccess(
            [.. items.Where(i => i.State == AccessState.Active).OrderBy(i => i.EndsAt).ThenBy(i => i.Title, StringComparer.CurrentCulture)],
            [.. items.Where(i => i.State is AccessState.Ended or AccessState.Refunded).OrderByDescending(i => i.EndsAt).ThenBy(i => i.Title, StringComparer.CurrentCulture)]);
    }
}
