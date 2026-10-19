using System.Net;
using System.Text.Json;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Access;

/// <summary>
/// ENT-03: an admin grants, changes and ends access on <c>/admin/akses</c>; the learner's next request follows ENT-02, and
/// every change leaves an audit row.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed class AdminAccessTests(PostgresFixture database)
{
    [Fact]
    public async Task Granting_an_exam_gives_full_access_at_once_and_ending_it_falls_back_to_the_trial()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var (admin, adminUser) = await AdminAsync(app);
        var learner = TestData.User().Build();
        using var learnerClient = await app.CreateSignedInClientAsync(learner, RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Paid(49_000, accessDays: 30).WithQuestions(4, trial: 2));
        Assert.Equal("cta-trial", await FirstCtaAsync(learnerClient, examId));

        using var granted = await PostAsync(admin, learner, "GrantExam", new() { ["examId"] = $"{examId}", ["until"] = "" });

        Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
        Assert.Equal("cta-start", await FirstCtaAsync(learnerClient, examId));
        var purchase = await PurchaseAsync(learner.Id, examId);
        Assert.Equal((ExamPurchaseStatus.Active, 0L, app.Clock.UtcNow.AddDays(30)), (purchase.Status, purchase.PricePaidIdr, purchase.AccessExpiresAt!.Value));

        using var ended = await PostAsync(admin, learner, "End", new() { ["kind"] = "Purchase", ["id"] = $"{purchase.Id}" });

        Assert.Equal(HttpStatusCode.Redirect, ended.StatusCode);
        Assert.Equal("cta-trial", await FirstCtaAsync(learnerClient, examId));
        Assert.Equal(ExamPurchaseStatus.Expired, (await PurchaseAsync(learner.Id, examId)).Status);

        var audit = await AuditAsync("ExamPurchase", purchase.Id);
        Assert.Equal(["Access.GrantExam", "Access.End"], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal(adminUser.Id, a.ActorUserId));
        using var changes = JsonDocument.Parse(audit[1].Changes!);
        Assert.Equal("Active", changes.RootElement.GetProperty("before").GetProperty("status").GetString());
        Assert.Equal("Expired", changes.RootElement.GetProperty("after").GetProperty("status").GetString());
        Assert.Equal(learner.Id, changes.RootElement.GetProperty("userId").GetGuid());
    }

    [Fact]
    public async Task Granting_again_extends_running_access_and_a_custom_date_lasts_the_whole_jakarta_day()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var (admin, _) = await AdminAsync(app);
        var learner = await SaveAsync(TestData.User().Build());
        var examId = await SeedAsync(TestData.Exam().Paid(49_000, accessDays: 30).WithQuestions(2, trial: 2));

        using (await PostAsync(admin, learner, "GrantExam", new() { ["examId"] = $"{examId}" }))
        using (await PostAsync(admin, learner, "GrantExam", new() { ["examId"] = $"{examId}" }))
        {
        }

        Assert.Equal(app.Clock.UtcNow.AddDays(60), (await PurchaseAsync(learner.Id, examId)).AccessExpiresAt);

        using (await PostAsync(admin, learner, "GrantExam", new() { ["examId"] = $"{examId}", ["until"] = "2027-01-31" }))
        {
        }

        var purchase = await PurchaseAsync(learner.Id, examId);
        Assert.Equal(new DateTime(2027, 1, 31, 16, 59, 59, DateTimeKind.Utc), purchase.AccessExpiresAt);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.ExamPurchases.CountAsync(p => p.UserId == learner.Id));
    }

    [Fact]
    public async Task Changing_the_end_date_reactivates_ended_access()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var (admin, _) = await AdminAsync(app);
        var learner = TestData.User().Build();
        using var learnerClient = await app.CreateSignedInClientAsync(learner, RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(2, trial: 2));
        using (await PostAsync(admin, learner, "GrantExam", new() { ["examId"] = $"{examId}" }))
        {
        }

        var id = (await PurchaseAsync(learner.Id, examId)).Id;
        using (await PostAsync(admin, learner, "End", new() { ["kind"] = "Purchase", ["id"] = $"{id}" }))
        {
        }

        Assert.Equal("cta-trial", await FirstCtaAsync(learnerClient, examId));

        using var changed = await PostAsync(admin, learner, "ChangeEnd", new() { ["kind"] = "Purchase", ["id"] = $"{id}", ["until"] = "2026-12-31" });

        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Equal("cta-start", await FirstCtaAsync(learnerClient, examId));
        Assert.Equal(new DateTime(2026, 12, 31, 16, 59, 59, DateTimeKind.Utc), (await PurchaseAsync(learner.Id, examId)).AccessExpiresAt);
        Assert.Equal(3, (await AuditAsync("ExamPurchase", id)).Count);
    }

    [Fact]
    public async Task Granting_a_plan_needs_a_price_then_opens_every_paid_exam_until_ended()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var (admin, _) = await AdminAsync(app);
        var learner = TestData.User().Build();
        using var learnerClient = await app.CreateSignedInClientAsync(learner, RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(2, trial: 2));
        long planId;
        await using (var db = database.CreateContext())
        {
            planId = (await db.Plans.SingleAsync(p => p.Code == PlanCode.Combined)).Id;
        }

        // The shared test database may already hold a price for this plan from another test; only check the
        // no-price message when there is none.
        if (!await HasPriceAsync(planId))
        {
            using var refused = await PostAsync(admin, learner, "GrantPlan", new() { ["planId"] = $"{planId}", ["days"] = "30" });
            var html = WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
            Assert.Contains("belum punya harga", html, StringComparison.Ordinal);
            await using var db = database.CreateContext();
            db.PlanPrices.Add(new PlanPrice { PlanId = planId, DurationMonths = 1, PriceIdr = 99_000 });
            await db.SaveChangesAsync();
        }

        using var granted = await PostAsync(admin, learner, "GrantPlan", new() { ["planId"] = $"{planId}", ["days"] = "7" });

        Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
        Assert.Equal("cta-start", await FirstCtaAsync(learnerClient, examId));
        Subscription subscription;
        await using (var db = database.CreateContext())
        {
            subscription = await db.Subscriptions.SingleAsync(s => s.UserId == learner.Id);
        }

        Assert.Equal((SubscriptionStatus.Active, app.Clock.UtcNow, app.Clock.UtcNow.AddDays(7)), (subscription.Status, subscription.CurrentPeriodStart!.Value, subscription.CurrentPeriodEnd!.Value));

        using var ended = await PostAsync(admin, learner, "End", new() { ["kind"] = "Subscription", ["id"] = $"{subscription.Id}" });

        Assert.Equal("cta-trial", await FirstCtaAsync(learnerClient, examId));
        Assert.Equal(["Access.GrantPlan", "Access.End"], (await AuditAsync("Subscription", subscription.Id)).Select(a => a.Action));
    }

    [Fact]
    public async Task Problems_are_shown_on_the_page()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var (admin, _) = await AdminAsync(app);
        var learner = await SaveAsync(TestData.User().Build());
        var free = await SeedAsync(TestData.Exam().Free().WithQuestions(1));
        var paid = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1));

        using var freeGrant = await PostAsync(admin, learner, "GrantExam", new() { ["examId"] = $"{free}" });
        using var pastGrant = await PostAsync(admin, learner, "GrantExam", new() { ["examId"] = $"{paid}", ["until"] = "2026-09-30" });
        var freeHtml = WebUtility.HtmlDecode(await freeGrant.Content.ReadAsStringAsync());
        var pastHtml = WebUtility.HtmlDecode(await pastGrant.Content.ReadAsStringAsync());

        Assert.Contains("Ujian gratis tidak perlu diberikan.", freeHtml, StringComparison.Ordinal);
        Assert.Contains("Tanggal akhir harus setelah hari ini.", pastHtml, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.False(await db.ExamPurchases.AnyAsync(p => p.UserId == learner.Id));
    }

    [Fact]
    public async Task Lookup_is_by_exact_email_ignoring_case_and_lists_access_with_jakarta_dates()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var (admin, _) = await AdminAsync(app);
        var learner = await SaveAsync(TestData.User().Build());
        var examId = await SeedAsync(TestData.Exam().Titled("Tryout Akses " + TestData.Unique()).Paid(49_000).WithQuestions(1, trial: 1));
        await using (var db = database.CreateContext())
        {
            db.ExamPurchases.Add(new ExamPurchase
            {
                UserId = learner.Id,
                ExamId = examId,
                PricePaidIdr = 49_000,
                Status = ExamPurchaseStatus.Active,
                PurchasedAt = TestClock.DefaultStart,
                AccessExpiresAt = new DateTime(2026, 10, 11, 16, 59, 59, DateTimeKind.Utc),
            });
            await db.SaveChangesAsync();
        }

        var found = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/akses?email={Uri.EscapeDataString(learner.Email!.ToUpperInvariant())}", UriKind.Relative)));
        var partial = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/akses?email={Uri.EscapeDataString(learner.Email![..^3])}", UriKind.Relative)));

        Assert.Contains("data-testid=\"access-user\"", found, StringComparison.Ordinal);
        Assert.Contains("Sampai 11 Okt 2026 23.59 WIB", found, StringComparison.Ordinal);
        Assert.Contains("10 hari lagi", found, StringComparison.Ordinal);
        Assert.Contains("Rp49.000", found, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"access-no-user\"", partial, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Learners_cannot_open_the_access_page()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var response = await learner.GetAsync(new Uri("/admin/akses", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Helpers ----------

    private static async Task<(HttpClient Client, User User)> AdminAsync(AppFactory app)
    {
        var user = TestData.User().Build();
        return (await app.CreateSignedInClientAsync(user, RoleNames.Admin), user);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient admin, User learner, string handler, Dictionary<string, string> fields)
    {
        var page = $"/admin/akses?email={Uri.EscapeDataString(learner.Email!)}";
        fields["userId"] = learner.Id.ToString();
        fields["email"] = learner.Email!;
        return admin.PostFormAsync(page, fields, $"/admin/akses?handler={handler}");
    }

    private static async Task<string?> FirstCtaAsync(HttpClient client, long examId)
    {
        var html = await client.GetStringAsync(new Uri($"/ujian/{examId}", UriKind.Relative));
        var match = System.Text.RegularExpressions.Regex.Match(html, "data-testid=\"(cta-[a-z]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<long> SeedAsync(Exam exam)
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(exam)).Id;
    }

    private async Task<User> SaveAsync(User user)
    {
        await using var db = database.CreateContext();
        return await db.SaveAsync(user);
    }

    private async Task<ExamPurchase> PurchaseAsync(Guid userId, long examId)
    {
        await using var db = database.CreateContext();
        return await db.ExamPurchases.SingleAsync(p => p.UserId == userId && p.ExamId == examId);
    }

    private async Task<bool> HasPriceAsync(long planId)
    {
        await using var db = database.CreateContext();
        return await db.PlanPrices.AnyAsync(p => p.PlanId == planId);
    }

    private async Task<List<Akusiap.Domain.Auditing.AuditLog>> AuditAsync(string entity, long id)
    {
        await using var db = database.CreateContext();
        var key = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return await db.AuditLogs.Where(a => a.EntityName == entity && a.EntityId == key).OrderBy(a => a.Id).ToListAsync();
    }
}
