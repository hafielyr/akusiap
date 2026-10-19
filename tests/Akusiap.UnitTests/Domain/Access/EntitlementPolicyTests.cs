using Akusiap.Domain.Access;
using Akusiap.Domain.Content;

namespace Akusiap.UnitTests.Domain.Access;

/// <summary>ENT-01: the entitlement rules in architecture.md, evaluated in order (first match wins).</summary>
public sealed class EntitlementPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 19, 5, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AdminId = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid LearnerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherLearnerId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid TeacherId = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private const string Token = "s3cr3t-share-token";

    // ---------- Rule 1: admin ----------

    [Fact]
    public void Rule1_admin_gets_admin_access_to_every_exam_even_taken_down()
    {
        var admin = Viewer(AdminId, isAdmin: true);

        Assert.Equal(Decision(AccessLevel.Admin, AccessReason.Admin, QuestionScope.All), Evaluate(admin, PaidExam()));
        Assert.Equal(AccessLevel.Admin, Evaluate(admin, PaidExam() with { Status = ExamStatus.TakenDown }).Level);
        Assert.True(Evaluate(admin, PaidExam() with { Status = ExamStatus.Draft }).CanStart);
    }

    // ---------- Rule 2: owner ----------

    [Theory]
    [InlineData(ExamStatus.Draft)]
    [InlineData(ExamStatus.Published)]
    [InlineData(ExamStatus.Unpublished)]
    public void Rule2_owner_gets_all_questions_at_any_status_except_taken_down(ExamStatus status)
    {
        var decision = Evaluate(Viewer(LearnerId), OwnExam(LearnerId) with { Status = status });

        Assert.Equal(Decision(AccessLevel.Owner, AccessReason.Owner, QuestionScope.All), decision);
        Assert.True(decision.CanStart);
    }

    [Fact]
    public void Rule2_owner_of_a_taken_down_exam_can_view_but_not_start()
    {
        var decision = Evaluate(Viewer(LearnerId), OwnExam(LearnerId) with { Status = ExamStatus.TakenDown });

        Assert.Equal(AccessLevel.Owner, decision.Level);
        Assert.Equal(AccessReason.OwnerTakenDown, decision.Reason);
        Assert.False(decision.CanStart);
    }

    [Fact]
    public void Rule2_unpublished_exam_is_open_to_its_owner_but_not_to_another_user()
    {
        var exam = OwnExam(LearnerId) with { Status = ExamStatus.Unpublished };

        Assert.Equal(AccessLevel.Owner, Evaluate(Viewer(LearnerId), exam).Level);
        Assert.Equal(Decision(AccessLevel.None, AccessReason.NotPublished, QuestionScope.None), Evaluate(Viewer(OtherLearnerId), exam));
    }

    [Fact]
    public void Rule2_guests_never_match_an_app_exam_without_owner()
    {
        Assert.NotEqual(AccessLevel.Owner, Evaluate(ViewerContext.Guest, PaidExam()).Level);
    }

    // ---------- Rule 3: taken down ----------

    [Fact]
    public void Rule3_taken_down_exam_is_closed_to_everyone_but_admin_and_owner_even_buyers_and_link_holders()
    {
        var exam = SharedExam(OtherLearnerId) with { Status = ExamStatus.TakenDown };
        var buyer = Viewer(LearnerId, purchases: [Purchase(exam.Id)]);

        Assert.Equal(Decision(AccessLevel.None, AccessReason.TakenDown, QuestionScope.None), Evaluate(buyer, exam, Token));
        Assert.Equal(AccessReason.TakenDown, Evaluate(ViewerContext.Guest, exam, Token).Reason);
    }

    // ---------- Rule 4: shared link ----------

    [Fact]
    public void Rule4_matching_token_on_a_shared_learner_exam_gives_shared_access_and_guests_must_give_a_name()
    {
        var exam = SharedExam(OtherLearnerId);

        var guest = Evaluate(ViewerContext.Guest, exam, Token);
        var member = Evaluate(Viewer(LearnerId), exam, Token);

        Assert.Equal(Decision(AccessLevel.Shared, AccessReason.SharedLink, QuestionScope.All, requiresGuestName: true), guest);
        Assert.Equal(Decision(AccessLevel.Shared, AccessReason.SharedLink, QuestionScope.All), member);
    }

    [Fact]
    public void Rule4_shared_access_does_not_need_the_exam_to_be_published()
    {
        Assert.Equal(AccessLevel.Shared, Evaluate(ViewerContext.Guest, SharedExam(OtherLearnerId) with { Status = ExamStatus.Draft }, Token).Level);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-token")]
    [InlineData("S3CR3T-SHARE-TOKEN")]
    public void Rule4_missing_or_wrong_token_gives_no_shared_access(string? token)
    {
        var decision = Evaluate(ViewerContext.Guest, SharedExam(OtherLearnerId) with { Status = ExamStatus.Draft }, token);

        Assert.Equal(Decision(AccessLevel.None, AccessReason.NotPublished, QuestionScope.None), decision);
    }

    [Fact]
    public void Rule4_needs_the_exam_to_be_shared_and_owned_by_a_learner()
    {
        var notShared = SharedExam(OtherLearnerId) with { IsShared = false, Status = ExamStatus.Draft };
        var teacherExam = SharedExam(TeacherId) with { OwnerKind = OwnerKind.Teacher, Status = ExamStatus.Draft };

        Assert.Equal(AccessLevel.None, Evaluate(ViewerContext.Guest, notShared, Token).Level);
        Assert.Equal(AccessLevel.None, Evaluate(ViewerContext.Guest, teacherExam, Token).Level);
    }

    // ---------- Rule 5: not published ----------

    [Theory]
    [InlineData(ExamStatus.Draft)]
    [InlineData(ExamStatus.Unpublished)]
    public void Rule5_unpublished_exams_are_closed_even_to_buyers_and_subscribers(ExamStatus status)
    {
        var exam = PaidExam() with { Status = status };
        var buyer = Viewer(LearnerId, purchases: [Purchase(exam.Id)]);
        var subscriber = Viewer(LearnerId, subscriptions: [Subscription(includesAllExams: true)]);

        Assert.Equal(Decision(AccessLevel.None, AccessReason.NotPublished, QuestionScope.None), Evaluate(buyer, exam));
        Assert.Equal(AccessReason.NotPublished, Evaluate(subscriber, exam).Reason);
    }

    // ---------- Rule 6: free exams ----------

    [Fact]
    public void Rule6_free_exam_is_full_for_registered_users()
    {
        Assert.Equal(Decision(AccessLevel.Full, AccessReason.FreeExam, QuestionScope.All), Evaluate(Viewer(LearnerId), FreeExam()));
    }

    [Fact]
    public void Rule6_guest_on_a_free_exam_is_asked_to_register()
    {
        Assert.Equal(Decision(AccessLevel.None, AccessReason.RegisterToUnlock, QuestionScope.None), Evaluate(ViewerContext.Guest, FreeExam()));
    }

    // ---------- Rule 7: purchase ----------

    [Fact]
    public void Rule7_active_unexpired_purchase_gives_full_access()
    {
        var exam = PaidExam();

        Assert.Equal(
            Decision(AccessLevel.Full, AccessReason.Purchased, QuestionScope.All),
            Evaluate(Viewer(LearnerId, purchases: [Purchase(exam.Id, expiresAt: Now.AddTicks(1))]), exam));
    }

    [Fact]
    public void Rule7_purchase_that_expires_exactly_now_gives_no_full_access()
    {
        var exam = PaidExam();

        Assert.Equal(TrialDecision, Evaluate(Viewer(LearnerId, purchases: [Purchase(exam.Id, expiresAt: Now)]), exam));
    }

    [Theory]
    [InlineData(ExamPurchaseStatus.AwaitingPayment)]
    [InlineData(ExamPurchaseStatus.Expired)]
    [InlineData(ExamPurchaseStatus.Abandoned)]
    [InlineData(ExamPurchaseStatus.Refunded)]
    public void Rule7_only_active_purchases_count(ExamPurchaseStatus status)
    {
        var exam = PaidExam();

        Assert.Equal(TrialDecision, Evaluate(Viewer(LearnerId, purchases: [Purchase(exam.Id, status)]), exam));
    }

    [Fact]
    public void Rule7_a_purchase_of_another_exam_does_not_count()
    {
        var exam = PaidExam();

        Assert.Equal(TrialDecision, Evaluate(Viewer(LearnerId, purchases: [Purchase(exam.Id + 1)]), exam));
    }

    // ---------- Rule 8: subscription ----------

    [Fact]
    public void Rule8_active_subscription_to_a_plan_with_all_exams_gives_full_access()
    {
        Assert.Equal(
            Decision(AccessLevel.Full, AccessReason.Subscription, QuestionScope.All),
            Evaluate(Viewer(LearnerId, subscriptions: [Subscription(includesAllExams: true)]), PaidExam()));
    }

    [Fact]
    public void Rule8_expired_subscription_gives_no_full_access()
    {
        var expiredByDate = Subscription(includesAllExams: true, periodEnd: Now);
        var expiredByStatus = Subscription(includesAllExams: true, status: SubscriptionStatus.Expired);

        Assert.Equal(TrialDecision, Evaluate(Viewer(LearnerId, subscriptions: [expiredByDate]), PaidExam()));
        Assert.Equal(TrialDecision, Evaluate(Viewer(LearnerId, subscriptions: [expiredByStatus]), PaidExam()));
    }

    [Theory]
    [InlineData(SubscriptionStatus.AwaitingPayment)]
    [InlineData(SubscriptionStatus.Abandoned)]
    [InlineData(SubscriptionStatus.Refunded)]
    public void Rule8_only_active_subscriptions_count(SubscriptionStatus status)
    {
        Assert.Equal(TrialDecision, Evaluate(Viewer(LearnerId, subscriptions: [Subscription(includesAllExams: true, status: status)]), PaidExam()));
    }

    [Fact]
    public void Rule8_plan_without_all_exams_gives_no_full_access()
    {
        Assert.Equal(TrialDecision, Evaluate(Viewer(TeacherId, subscriptions: [Subscription(includesAllExams: false)]), PaidExam()));
    }

    [Fact]
    public void Rule8_one_good_subscription_is_enough_among_several()
    {
        var viewer = Viewer(LearnerId, subscriptions:
        [
            Subscription(includesAllExams: false),
            Subscription(includesAllExams: true, status: SubscriptionStatus.Expired),
            Subscription(includesAllExams: true),
        ]);

        Assert.Equal(AccessReason.Subscription, Evaluate(viewer, PaidExam()).Reason);
    }

    [Fact]
    public void Rule7_purchase_wins_over_subscription_when_both_apply()
    {
        var exam = PaidExam();
        var viewer = Viewer(LearnerId, purchases: [Purchase(exam.Id)], subscriptions: [Subscription(includesAllExams: true)]);

        Assert.Equal(AccessReason.Purchased, Evaluate(viewer, exam).Reason);
    }

    // ---------- Rule 9: trial ----------

    [Fact]
    public void Rule9_paid_app_exam_with_trial_questions_gives_the_trial_to_guests_and_registered_users()
    {
        Assert.Equal(TrialDecision, Evaluate(ViewerContext.Guest, PaidExam()));
        Assert.Equal(TrialDecision, Evaluate(Viewer(LearnerId), PaidExam()));
    }

    // ---------- Rule 10: otherwise ----------

    [Fact]
    public void Rule10_paid_exam_without_trial_questions_gives_no_access()
    {
        Assert.Equal(
            Decision(AccessLevel.None, AccessReason.NoAccess, QuestionScope.None),
            Evaluate(Viewer(LearnerId), PaidExam() with { HasTrialQuestions = false }));
    }

    [Fact]
    public void Rule10_published_paid_exam_of_a_teacher_gets_no_trial()
    {
        var teacherExam = PaidExam() with { OwnerUserId = TeacherId, OwnerKind = OwnerKind.Teacher };

        Assert.Equal(AccessReason.NoAccess, Evaluate(Viewer(LearnerId), teacherExam).Reason);
    }

    [Fact]
    public void Times_must_be_utc()
    {
        Assert.Throws<ArgumentException>(() => EntitlementPolicy.Evaluate(ViewerContext.Guest, PaidExam(), null, DateTime.SpecifyKind(Now, DateTimeKind.Local)));
    }

    // ---------- The seven personas against the four kinds of exam ----------

    public static TheoryData<string, string, AccessLevel, AccessReason> PersonaMatrix => new()
    {
        // Persona,                 exam,          level,              reason
        { "Guest",                  "free",        AccessLevel.None,   AccessReason.RegisterToUnlock },
        { "Guest",                  "paid",        AccessLevel.Trial,  AccessReason.TrialAvailable },
        { "Guest",                  "unpublished", AccessLevel.None,   AccessReason.NotPublished },
        { "Guest",                  "shared",      AccessLevel.Shared, AccessReason.SharedLink },
        { "Free learner",           "free",        AccessLevel.Full,   AccessReason.FreeExam },
        { "Free learner",           "paid",        AccessLevel.Trial,  AccessReason.TrialAvailable },
        { "Free learner",           "unpublished", AccessLevel.None,   AccessReason.NotPublished },
        { "Free learner",           "shared",      AccessLevel.Shared, AccessReason.SharedLink },
        { "Single-exam buyer",      "free",        AccessLevel.Full,   AccessReason.FreeExam },
        { "Single-exam buyer",      "paid",        AccessLevel.Full,   AccessReason.Purchased },
        { "Single-exam buyer",      "unpublished", AccessLevel.None,   AccessReason.NotPublished },
        { "Single-exam buyer",      "shared",      AccessLevel.Shared, AccessReason.SharedLink },
        { "Learner subscriber",     "free",        AccessLevel.Full,   AccessReason.FreeExam },
        { "Learner subscriber",     "paid",        AccessLevel.Full,   AccessReason.Subscription },
        { "Learner subscriber",     "unpublished", AccessLevel.None,   AccessReason.NotPublished },
        { "Learner subscriber",     "shared",      AccessLevel.Shared, AccessReason.SharedLink },
        { "Teacher",                "free",        AccessLevel.Full,   AccessReason.FreeExam },
        { "Teacher",                "paid",        AccessLevel.Trial,  AccessReason.TrialAvailable },
        { "Teacher",                "unpublished", AccessLevel.None,   AccessReason.NotPublished },
        { "Teacher",                "shared",      AccessLevel.Shared, AccessReason.SharedLink },
        { "Learner + teacher",      "free",        AccessLevel.Full,   AccessReason.FreeExam },
        { "Learner + teacher",      "paid",        AccessLevel.Full,   AccessReason.Subscription },
        { "Learner + teacher",      "unpublished", AccessLevel.None,   AccessReason.NotPublished },
        { "Learner + teacher",      "shared",      AccessLevel.Shared, AccessReason.SharedLink },
        { "Admin",                  "free",        AccessLevel.Admin,  AccessReason.Admin },
        { "Admin",                  "paid",        AccessLevel.Admin,  AccessReason.Admin },
        { "Admin",                  "unpublished", AccessLevel.Admin,  AccessReason.Admin },
        { "Admin",                  "shared",      AccessLevel.Admin,  AccessReason.Admin },
    };

    [Theory]
    [MemberData(nameof(PersonaMatrix))]
    public void Persona_gets_the_expected_access(string persona, string exam, AccessLevel level, AccessReason reason)
    {
        var (facts, token) = ExamFor(exam);

        var decision = Evaluate(PersonaViewer(persona), facts, token);

        Assert.Equal((level, reason), (decision.Level, decision.Reason));
        Assert.Equal(level == AccessLevel.None ? QuestionScope.None : level == AccessLevel.Trial ? QuestionScope.TrialOnly : QuestionScope.All, decision.Scope);
        Assert.Equal(level != AccessLevel.None, decision.CanStart);
    }

    // ---------- Helpers ----------

    private static readonly AccessDecision TrialDecision = Decision(AccessLevel.Trial, AccessReason.TrialAvailable, QuestionScope.TrialOnly);

    private static AccessDecision Evaluate(ViewerContext viewer, ExamAccessFacts exam, string? shareToken = null) =>
        EntitlementPolicy.Evaluate(viewer, exam, shareToken, Now);

    private static AccessDecision Decision(AccessLevel level, AccessReason reason, QuestionScope scope, bool requiresGuestName = false) =>
        new(level, reason, scope) { RequiresGuestName = requiresGuestName };

    private static ViewerContext Viewer(
        Guid userId, bool isAdmin = false, PurchaseFact[]? purchases = null, SubscriptionFact[]? subscriptions = null) =>
        new(userId, isAdmin, purchases ?? [], subscriptions ?? []);

    private static PurchaseFact Purchase(long examId, ExamPurchaseStatus status = ExamPurchaseStatus.Active, DateTime? expiresAt = null) =>
        new(examId, status, expiresAt ?? Now.AddDays(10));

    private static SubscriptionFact Subscription(bool includesAllExams, SubscriptionStatus status = SubscriptionStatus.Active, DateTime? periodEnd = null) =>
        new(status, periodEnd ?? Now.AddDays(10), includesAllExams);

    private static ExamAccessFacts PaidExam() =>
        new(Id: 10, OwnerUserId: null, OwnerKind.App, ExamStatus.Published, IsFree: false, IsShared: false, ShareToken: null, HasTrialQuestions: true);

    private static ExamAccessFacts FreeExam() => PaidExam() with { Id = 11, IsFree = true, HasTrialQuestions = false };

    private static ExamAccessFacts OwnExam(Guid owner) =>
        new(Id: 12, owner, OwnerKind.Learner, ExamStatus.Draft, IsFree: true, IsShared: false, ShareToken: null, HasTrialQuestions: false);

    private static ExamAccessFacts SharedExam(Guid owner) => OwnExam(owner) with { Id = 13, IsShared = true, ShareToken = Token };

    private static (ExamAccessFacts Exam, string? Token) ExamFor(string kind) => kind switch
    {
        "free" => (FreeExam(), null),
        "paid" => (PaidExam(), null),
        "unpublished" => (PaidExam() with { Status = ExamStatus.Unpublished }, null),
        "shared" => (SharedExam(OtherLearnerId), Token),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private static ViewerContext PersonaViewer(string persona) => persona switch
    {
        "Guest" => ViewerContext.Guest,
        "Free learner" => Viewer(LearnerId),
        // Bought the paid exam (and, to show rule 5 wins, the unpublished one too: same id).
        "Single-exam buyer" => Viewer(LearnerId, purchases: [Purchase(PaidExam().Id)]),
        "Learner subscriber" => Viewer(LearnerId, subscriptions: [Subscription(includesAllExams: true)]),
        // Teacher plan: authoring and classes, not all exams.
        "Teacher" => Viewer(TeacherId, subscriptions: [Subscription(includesAllExams: false)]),
        "Learner + teacher" => Viewer(TeacherId, subscriptions: [Subscription(includesAllExams: true)]),
        "Admin" => Viewer(AdminId, isAdmin: true),
        _ => throw new ArgumentOutOfRangeException(nameof(persona), persona, null),
    };
}
