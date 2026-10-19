using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Catalog;

/// <summary>
/// CAT-02: the exam page at <c>/ujian/{id}</c>. Each access decision renders its buttons; viewers get their access the
/// same way as in the ENT-02 tests (purchases, subscriptions, roles).
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed partial class ExamDetailTests(PostgresFixture database)
{
    [Fact]
    public async Task Guest_on_a_paid_exam_gets_the_trial_then_buy_and_subscribe()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient();
        var examId = await SeedAsync(TestData.Exam().Paid(49_000, accessDays: 60).WithQuestions(6, trial: 4));

        var html = await GetHtmlAsync(guest, examId);

        Assert.Equal(["cta-trial", "cta-buy", "cta-subscribe"], Ctas(html));
        Assert.Contains("Coba 4 soal gratis", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ujian/{examId}/mulai\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/checkout?exam={examId}\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/harga?ujian={examId}\"", html, StringComparison.Ordinal);
        Assert.Contains("Rp49.000", html, StringComparison.Ordinal);
        Assert.Contains("60 hari akses", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guest_on_a_free_exam_is_asked_to_register_and_comes_back_after()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var examId = await SeedAsync(TestData.Exam().Free().WithQuestions(3));

        var html = await GetHtmlAsync(guest, examId);
        var registerUrl = $"/akun/daftar?ReturnUrl=%2Fujian%2F{examId}";

        Assert.Equal(["cta-register"], Ctas(html));
        Assert.Contains("Daftar gratis untuk membuka", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"{registerUrl}\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/akun/masuk?ReturnUrl=%2Fujian%2F{examId}\"", html, StringComparison.Ordinal);

        var user = TestData.User().Build();
        using var registered = await guest.PostFormAsync(registerUrl, new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email!,
            ["Input.DisplayName"] = user.DisplayName,
            ["Input.Password"] = "sandi-baru-2026",
        });

        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        Assert.Equal($"/ujian/{examId}", registered.Headers.Location?.OriginalString);
        Assert.Equal(["cta-start"], Ctas(await GetHtmlAsync(guest, examId)));
    }

    [Fact]
    public async Task Register_ignores_a_return_url_to_another_site()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var user = TestData.User().Build();

        using var registered = await guest.PostFormAsync("/akun/daftar?ReturnUrl=https%3A%2F%2Fevil.example%2F", new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email!,
            ["Input.DisplayName"] = user.DisplayName,
            ["Input.Password"] = "sandi-baru-2026",
        });

        Assert.Equal("/ujian", registered.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Registered_user_on_a_free_exam_starts_practice()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Free().WithQuestions(3));

        var html = await GetHtmlAsync(learner, examId);

        Assert.Equal(["cta-start"], Ctas(html));
        Assert.Contains("Mulai latihan", html, StringComparison.Ordinal);
        Assert.Contains("Gratis untuk pengguna terdaftar", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Registered_user_without_a_purchase_gets_the_trial()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(4, trial: 2));

        Assert.Equal(["cta-trial", "cta-buy", "cta-subscribe"], Ctas(await GetHtmlAsync(learner, examId)));
    }

    [Fact]
    public async Task Purchase_holder_starts_practice_and_sees_akses_anda()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var buyer = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(4, trial: 2));
        await AddPurchaseAsync(user.Id, examId, TestClock.DefaultStart.AddDays(10));

        var html = await GetHtmlAsync(buyer, examId);

        Assert.Equal(["cta-start"], Ctas(html));
        Assert.Contains("Akses Anda", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expired_purchase_falls_back_to_the_trial()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var buyer = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(4, trial: 2));
        await AddPurchaseAsync(user.Id, examId, TestClock.DefaultStart.AddDays(-1));

        var html = await GetHtmlAsync(buyer, examId);

        Assert.Equal(["cta-trial", "cta-buy", "cta-subscribe"], Ctas(html));
        Assert.DoesNotContain("Akses Anda", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PlanCode.Learner, new[] { "cta-start" })]
    [InlineData(PlanCode.Teacher, new[] { "cta-trial", "cta-buy", "cta-subscribe" })]
    public async Task Subscription_opens_the_exam_only_when_its_plan_includes_all_exams(PlanCode plan, string[] expected)
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var subscriber = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(4, trial: 2));
        await AddSubscriptionAsync(user.Id, plan);

        Assert.Equal(expected, Ctas(await GetHtmlAsync(subscriber, examId)));
    }

    [Fact]
    public async Task Paid_exam_without_trial_questions_offers_only_buy_and_subscribe()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(3, trial: 0));

        Assert.Equal(["cta-buy", "cta-subscribe"], Ctas(await GetHtmlAsync(learner, examId)));
    }

    [Fact]
    public async Task Admin_previews_a_draft_and_can_start_it()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var examId = await SeedAsync(TestData.Exam().Paid(49_000).Draft().WithQuestions(3, trial: 3));

        var html = await GetHtmlAsync(admin, examId);

        Assert.Equal(["cta-start"], Ctas(html));
        Assert.Contains("data-testid=\"preview-notice\"", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ExamStatus.Draft)]
    [InlineData(ExamStatus.Unpublished)]
    [InlineData(ExamStatus.TakenDown)]
    public async Task Exams_out_of_the_catalog_are_not_found(ExamStatus status)
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var guest = app.CreateClient();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Free().WithStatus(status).WithQuestions(2));

        using var forGuest = await guest.GetAsync(new Uri($"/ujian/{examId}", UriKind.Relative));
        using var forLearner = await learner.GetAsync(new Uri($"/ujian/{examId}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, forGuest.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, forLearner.StatusCode);
    }

    [Fact]
    public async Task Learner_exams_and_unknown_ids_are_not_found()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient();
        User owner;
        await using (var db = database.CreateContext())
        {
            owner = await db.SaveAsync(TestData.User().Build());
        }

        var learnerExam = await SeedAsync(TestData.Exam().OwnedBy(owner).WithQuestions(2));

        using var own = await guest.GetAsync(new Uri($"/ujian/{learnerExam}", UriKind.Relative));
        using var unknown = await guest.GetAsync(new Uri("/ujian/987654321", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, own.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Page_shows_description_and_topics_with_active_question_counts()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient();
        long examId;
        string firstTopic;
        string secondTopic;
        await using (var db = database.CreateContext())
        {
            var exam = TestData.Exam().Paid(49_000).WithQuestions(6, trial: 6).Build();
            exam.Description = "Latihan SKD lengkap.\nBaris kedua.";
            var questions = exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.Question!).ToList();
            firstTopic = questions[0].Topic!.Name;
            var other = new Topic { Subject = exam.Subject, Name = "Topik " + TestData.Unique() };
            secondTopic = other.Name;
            questions[4].Topic = other;
            questions[5].Topic = other;
            questions[0].Status = QuestionStatus.Retired;
            examId = (await db.SaveAsync(exam)).Id;
        }

        var html = WebUtility.HtmlDecode(await GetHtmlAsync(guest, examId));

        Assert.Contains("Latihan SKD lengkap.\nBaris kedua.", html, StringComparison.Ordinal);
        Assert.Contains("5 soal aktif", html, StringComparison.Ordinal);
        Assert.Equal([(firstTopic, 3), (secondTopic, 2)], Topics(html));
        Assert.Contains("Coba 5 soal gratis", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalog_cards_link_to_the_exam_page()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient();
        var token = TestData.Unique();
        var examId = await SeedAsync(TestData.Exam().Titled($"Tautan {token}").WithQuestions(2));

        var html = await guest.GetStringAsync(new Uri($"/ujian?q={token}", UriKind.Relative));

        Assert.Contains($"href=\"/ujian/{examId}\"", html, StringComparison.Ordinal);
    }

    // ---------- Helpers ----------

    private async Task<long> SeedAsync(Exam exam)
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(exam)).Id;
    }

    private static async Task<string> GetHtmlAsync(HttpClient client, long examId)
    {
        using var response = await client.GetAsync(new Uri($"/ujian/{examId}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static List<string> Ctas(string html) =>
        [.. CtaPattern().Matches(html).Select(m => m.Groups[1].Value)];

    private static List<(string Name, int Count)> Topics(string html)
    {
        var list = html[html.IndexOf("data-testid=\"exam-topics\"", StringComparison.Ordinal)..];
        list = list[..list.IndexOf("</ul>", StringComparison.Ordinal)];
        return [.. TopicPattern().Matches(list).Select(m => (m.Groups[1].Value, int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)))];
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
            PurchasedAt = TestClock.DefaultStart.AddDays(-20),
            AccessExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync();
    }

    private async Task AddSubscriptionAsync(Guid userId, PlanCode code)
    {
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

    [GeneratedRegex("data-testid=\"(cta-[a-z]+)\"")]
    private static partial Regex CtaPattern();

    [GeneratedRegex("<li[^>]*>\\s*<span>([^<]+)</span>\\s*<span[^>]*>(\\d+) soal</span>")]
    private static partial Regex TopicPattern();
}
