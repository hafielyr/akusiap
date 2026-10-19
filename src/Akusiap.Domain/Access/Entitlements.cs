using Akusiap.Domain.Content;

namespace Akusiap.Domain.Access;

public enum AccessLevel
{
    None,
    Trial,
    Full,
    Owner,
    Admin,
    Shared,
}

/// <summary>Which questions an attempt may use. <see cref="None"/> goes with <see cref="AccessLevel.None"/>.</summary>
public enum QuestionScope
{
    None,
    TrialOnly,
    All,
}

/// <summary>Why the decision was made; pages use it to pick the message or call to action.</summary>
public enum AccessReason
{
    Admin,
    Owner,

    /// <summary>The owner may view a taken-down exam but not start it.</summary>
    OwnerTakenDown,
    TakenDown,
    SharedLink,
    NotPublished,
    FreeExam,

    /// <summary>A guest on a free exam: show "register free to unlock" (D-05).</summary>
    RegisterToUnlock,
    Purchased,
    Subscription,
    TrialAvailable,
    NoAccess,
}

/// <summary>An exam purchase of the viewer. The loader may pass every purchase; the policy checks status and expiry.</summary>
public sealed record PurchaseFact(long ExamId, ExamPurchaseStatus Status, DateTime? AccessExpiresAt);

/// <summary>A subscription of the viewer with its plan's all-exams flag.</summary>
public sealed record SubscriptionFact(SubscriptionStatus Status, DateTime? CurrentPeriodEnd, bool IncludesAllExams);

/// <summary>Who is asking. Guests have no user id.</summary>
public sealed record ViewerContext(
    Guid? UserId,
    bool IsAdmin,
    IReadOnlyList<PurchaseFact> Purchases,
    IReadOnlyList<SubscriptionFact> Subscriptions)
{
    public static ViewerContext Guest { get; } = new(null, false, [], []);

    public bool IsRegistered => UserId is not null;
}

/// <summary>The exam fields the rules need.</summary>
public sealed record ExamAccessFacts(
    long Id,
    Guid? OwnerUserId,
    OwnerKind OwnerKind,
    ExamStatus Status,
    bool IsFree,
    bool IsShared,
    string? ShareToken,
    bool HasTrialQuestions);

public sealed record AccessDecision(AccessLevel Level, AccessReason Reason, QuestionScope Scope)
{
    /// <summary>Shared access for a guest: they must enter a name before starting.</summary>
    public bool RequiresGuestName { get; init; }

    /// <summary>Whether an attempt may be started (taken-down exams can be viewed by their owner but not started).</summary>
    public bool CanStart => Level != AccessLevel.None && Reason != AccessReason.OwnerTakenDown;
}
