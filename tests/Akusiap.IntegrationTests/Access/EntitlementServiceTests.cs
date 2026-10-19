using System.Data.Common;
using Akusiap.Application.Access;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Persistence;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Akusiap.IntegrationTests.Access;

/// <summary>
/// ENT-02 against a seeded paid exam (40 questions, 30 trial; one trial and one non-trial question retired):
/// guest, free user, purchase holder, subscriber and admin, the question scope, and one viewer query per request.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed class EntitlementServiceTests(PostgresFixture database)
{
    private const int ActiveQuestions = 38;
    private const int ActiveTrialQuestions = 29;

    [Fact]
    public async Task Guest_gets_the_trial_and_only_active_trial_questions()
    {
        var exam = await SeedPaidExamAsync();

        var (decision, ids) = await EvaluateAsync(FakeCurrentUser.Guest, exam.Id);

        Assert.Equal((AccessLevel.Trial, AccessReason.TrialAvailable), (decision.Level, decision.Reason));
        Assert.Equal(ActiveTrialQuestions, ids.Count);
        Assert.DoesNotContain(exam.RetiredIds[0], ids);
    }

    [Fact]
    public async Task Free_user_without_a_purchase_gets_the_trial()
    {
        var exam = await SeedPaidExamAsync();
        var user = await SeedUserAsync();

        var (decision, ids) = await EvaluateAsync(new FakeCurrentUser(user.Id), exam.Id);

        Assert.Equal(AccessLevel.Trial, decision.Level);
        Assert.Equal(ActiveTrialQuestions, ids.Count);
    }

    [Fact]
    public async Task Purchase_holder_gets_every_active_question_and_never_retired_ones()
    {
        var exam = await SeedPaidExamAsync();
        var user = await SeedUserAsync();
        await AddPurchaseAsync(user.Id, exam.Id, expiresAt: TestClock.DefaultStart.AddDays(30));

        var (decision, ids) = await EvaluateAsync(new FakeCurrentUser(user.Id), exam.Id);

        Assert.Equal((AccessLevel.Full, AccessReason.Purchased, QuestionScope.All), (decision.Level, decision.Reason, decision.Scope));
        Assert.Equal(ActiveQuestions, ids.Count);
        Assert.DoesNotContain(exam.RetiredIds[0], ids);
        Assert.DoesNotContain(exam.RetiredIds[1], ids);
        Assert.Equal(exam.OrderedActiveIds, ids);
    }

    [Fact]
    public async Task Expired_purchase_falls_back_to_the_trial()
    {
        var exam = await SeedPaidExamAsync();
        var user = await SeedUserAsync();
        await AddPurchaseAsync(user.Id, exam.Id, expiresAt: TestClock.DefaultStart);

        var (decision, _) = await EvaluateAsync(new FakeCurrentUser(user.Id), exam.Id);

        Assert.Equal(AccessLevel.Trial, decision.Level);
    }

    [Fact]
    public async Task Subscriber_to_a_plan_with_all_exams_gets_full_access()
    {
        var exam = await SeedPaidExamAsync();
        var user = await SeedUserAsync();
        await AddSubscriptionAsync(user.Id, PlanCode.Learner);

        var (decision, ids) = await EvaluateAsync(new FakeCurrentUser(user.Id), exam.Id);

        Assert.Equal((AccessLevel.Full, AccessReason.Subscription), (decision.Level, decision.Reason));
        Assert.Equal(ActiveQuestions, ids.Count);
    }

    [Fact]
    public async Task Teacher_plan_subscriber_gets_only_the_trial()
    {
        var exam = await SeedPaidExamAsync();
        var user = await SeedUserAsync();
        await AddSubscriptionAsync(user.Id, PlanCode.Teacher);

        var (decision, _) = await EvaluateAsync(new FakeCurrentUser(user.Id), exam.Id);

        Assert.Equal(AccessLevel.Trial, decision.Level);
    }

    [Fact]
    public async Task Admin_gets_admin_access_to_every_active_question()
    {
        var exam = await SeedPaidExamAsync();
        var user = await SeedUserAsync();

        var (decision, ids) = await EvaluateAsync(new FakeCurrentUser(user.Id, IsAdmin: true), exam.Id);

        Assert.Equal(AccessLevel.Admin, decision.Level);
        Assert.Equal(ActiveQuestions, ids.Count);
    }

    [Fact]
    public async Task Free_exam_asks_guests_to_register_and_opens_for_users()
    {
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync<Exam>(TestData.Exam().Free().WithQuestions(5))).Id;
        }

        var user = await SeedUserAsync();

        Assert.Equal(AccessReason.RegisterToUnlock, (await EvaluateAsync(FakeCurrentUser.Guest, examId)).Decision.Reason);
        Assert.Equal(AccessReason.FreeExam, (await EvaluateAsync(new FakeCurrentUser(user.Id), examId)).Decision.Reason);
    }

    [Fact]
    public async Task Viewer_context_is_loaded_with_one_query_per_request_and_none_for_guests()
    {
        var first = await SeedPaidExamAsync();
        var second = await SeedPaidExamAsync();
        var user = await SeedUserAsync();
        await AddPurchaseAsync(user.Id, first.Id, expiresAt: TestClock.DefaultStart.AddDays(30));
        await AddSubscriptionAsync(user.Id, PlanCode.Teacher);
        var counter = new ViewerQueryCounter();

        // One "request": a single scoped service asked about two exams.
        await using (var db = CountingContext(counter))
        {
            var service = new EntitlementService(db, new FakeCurrentUser(user.Id), new TestClock());
            var a = await service.GetAsync(first.Id, null, CancellationToken.None);
            var b = await service.GetAsync(second.Id, null, CancellationToken.None);
            Assert.Equal((AccessLevel.Full, AccessLevel.Trial), (a!.Decision.Level, b!.Decision.Level));
        }

        Assert.Equal(1, counter.Count);

        await using (var db = CountingContext(counter))
        {
            var guest = new EntitlementService(db, FakeCurrentUser.Guest, new TestClock());
            await guest.GetAsync(first.Id, null, CancellationToken.None);
        }

        Assert.Equal(1, counter.Count);
    }

    [Fact]
    public async Task Unknown_exam_returns_null()
    {
        await using var db = database.CreateContext();
        var service = new EntitlementService(db, FakeCurrentUser.Guest, new TestClock());

        Assert.Null(await service.GetAsync(999_999_999, null, CancellationToken.None));
    }

    // ---------- Seeding ----------

    private sealed record SeededExam(long Id, long[] RetiredIds, IReadOnlyList<long> OrderedActiveIds);

    /// <summary>A published paid exam: 40 questions, 30 trial; question 1 (trial) and 40 (not trial) retired.</summary>
    private async Task<SeededExam> SeedPaidExamAsync()
    {
        await using var db = database.CreateContext();
        var exam = await db.SaveAsync<Exam>(TestData.Exam().Paid(49_000).WithQuestions(40, trial: 30));
        var links = exam.Questions.OrderBy(q => q.SortOrder).ToList();
        links[0].Question!.Status = QuestionStatus.Retired;
        links[^1].Question!.Status = QuestionStatus.Retired;
        await db.SaveChangesAsync();
        return new SeededExam(exam.Id, [links[0].QuestionId, links[^1].QuestionId], links.Skip(1).SkipLast(1).Select(l => l.QuestionId).ToList());
    }

    private async Task<User> SeedUserAsync()
    {
        await using var db = database.CreateContext();
        return await db.SaveAsync(TestData.User().Build());
    }

    private async Task AddPurchaseAsync(Guid userId, long examId, DateTime expiresAt)
    {
        await using var db = database.CreateContext();
        db.ExamPurchases.Add(new ExamPurchase
        {
            UserId = userId,
            ExamId = examId,
            PricePaidIdr = 49_000,
            Status = ExamPurchaseStatus.Active,
            PurchasedAt = TestClock.DefaultStart.AddDays(-1),
            AccessExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
    }

    private async Task AddSubscriptionAsync(Guid userId, PlanCode code)
    {
        await using (var app = new AppFactory(database))
        {
            await app.SeedReferenceDataAsync();
        }

        await using var db = database.CreateContext();
        var plan = await db.Plans.SingleAsync(p => p.Code == code);
        var price = await db.PlanPrices.FirstOrDefaultAsync(p => p.PlanId == plan.Id)
            ?? db.PlanPrices.Add(new PlanPrice { PlanId = plan.Id, DurationMonths = 1, PriceIdr = 99_000 }).Entity;
        db.Subscriptions.Add(new Subscription
        {
            UserId = userId,
            PlanId = plan.Id,
            PlanPrice = price,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = TestClock.DefaultStart.AddDays(-1),
            CurrentPeriodEnd = TestClock.DefaultStart.AddDays(30),
            CreatedAt = TestClock.DefaultStart,
        });
        await db.SaveChangesAsync();
    }

    // ---------- Evaluation ----------

    private async Task<(AccessDecision Decision, IReadOnlyList<long> Ids)> EvaluateAsync(FakeCurrentUser viewer, long examId)
    {
        await using var db = database.CreateContext();
        var result = await new EntitlementService(db, viewer, new TestClock()).GetAsync(examId, null, CancellationToken.None);
        var ids = await new ExamQuestionScope(db).GetQuestionIdsAsync(examId, result!.Decision.Scope, CancellationToken.None);
        return (result.Decision, ids);
    }

    private AppDbContext CountingContext(ViewerQueryCounter counter) =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(counter)
            .Options);

    /// <summary>Counts executed commands carrying the viewer query tag (the "EF logging" check of the AC).</summary>
    private sealed class ViewerQueryCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            if (command.CommandText.Contains(EntitlementService.ViewerQueryTag, StringComparison.Ordinal))
            {
                Count++;
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
