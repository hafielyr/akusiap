using System.Net;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Catalog;

/// <summary>
/// CPNS-01 and CPNS-02: the upcoming tests list (search, category, soonest first, past and hidden tests left out), a
/// test's page with its package (buy for visitors, study tools with access) and the admin form.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed class UpcomingTestsTests(PostgresFixture database)
{
    [Fact]
    public async Task The_list_shows_published_upcoming_tests_soonest_first()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient();
        var tag = TestData.Unique();
        await SeedAsync(Test(tag, "later", new DateOnly(2027, 3, 1)), Test(tag, "sooner", new DateOnly(2026, 12, 1)), Test(tag, "nodate", null),
            Test(tag, "past", new DateOnly(2026, 9, 1)), Test(tag, "hidden", new DateOnly(2026, 12, 2), published: false));

        var html = await guest.GetStringAsync(new Uri($"/jadwal-tes?q={tag}", UriKind.Relative));

        var sooner = html.IndexOf($"{tag}-sooner", StringComparison.Ordinal);
        var later = html.IndexOf($"{tag}-later", StringComparison.Ordinal);
        var nodate = html.IndexOf($"{tag}-nodate", StringComparison.Ordinal);
        Assert.True(sooner > 0 && sooner < later && later < nodate);
        Assert.DoesNotContain($"{tag}-past", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"{tag}-hidden", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"tests-total\">3 tes", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_and_category_narrow_the_list_and_htmx_gets_only_the_list()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient();
        var tag = TestData.Unique();
        var bumn = Test(tag, "bumn", new DateOnly(2027, 5, 1));
        bumn.Category = UpcomingTestCategory.Bumn;
        await SeedAsync(bumn, Test(tag, "cpns", new DateOnly(2027, 1, 1)));

        var html = await guest.GetStringAsync(new Uri($"/jadwal-tes?q={tag}&kategori=Bumn", UriKind.Relative));
        Assert.Contains($"{tag}-bumn", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"{tag}-cpns", html, StringComparison.Ordinal);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/jadwal-tes?q={tag}-zzz", UriKind.Relative));
        request.Headers.Add("HX-Request", "true");
        using var response = await guest.SendAsync(request);
        var fragment = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<div id=\"test-list\"", fragment.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("data-testid=\"tests-empty\"", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_visitor_sees_the_dates_outline_and_package_offer()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient();
        var exam = TestData.Exam().Paid(50_000, accessDays: 180).WithQuestions(30, trial: 20).Build();
        var test = Test(TestData.Unique(), "cpns", new DateOnly(2027, 1, 18));
        test.Outline = "Format SKD\n110 soal\n\nTWK\nNasionalisme";
        test.SourceUrl = "https://example.com/jadwal";
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(exam);
            test.ExamId = exam.Id;
            await db.SaveAsync(test);
        }

        var html = WebUtility.HtmlDecode(await guest.GetStringAsync(new Uri($"/jadwal-tes/{test.Slug}", UriKind.Relative)));

        Assert.Contains("Januari 2027", html, StringComparison.Ordinal);
        Assert.DoesNotContain("18 Januari 2027", html, StringComparison.Ordinal); // an estimate shows month and year only
        Assert.Contains("data-testid=\"test-status\">Perkiraan", html, StringComparison.Ordinal);
        Assert.Contains("Nasionalisme", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"package-price\">Rp50.000", html, StringComparison.Ordinal);
        Assert.Contains($"/checkout?exam={exam.Id}", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"cta-trial\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"study-tools\"", html, StringComparison.Ordinal);

        var examPage = await guest.GetStringAsync(new Uri($"/ujian/{exam.Id}", UriKind.Relative));
        Assert.Contains($"href=\"/jadwal-tes/{test.Slug}\"", examPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_buyer_gets_the_study_tools_instead_of_the_offer()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var exam = TestData.Exam().Paid(50_000, accessDays: 180).WithQuestions(30, trial: 20).Build();
        var test = Test(TestData.Unique(), "cpns", new DateOnly(2027, 1, 18));
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(exam);
            test.ExamId = exam.Id;
            await db.SaveAsync(test);
            var userId = await db.Users.Where(u => u.Email == user.Email).Select(u => u.Id).SingleAsync();
            await db.SaveAsync(new ExamPurchase
            {
                UserId = userId,
                ExamId = exam.Id,
                Status = ExamPurchaseStatus.Active,
                PricePaidIdr = 50_000,
                PurchasedAt = TestData.Now,
                AccessExpiresAt = TestData.Now.AddDays(180),
            });
        }

        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"/jadwal-tes/{test.Slug}", UriKind.Relative)));

        Assert.Contains("data-testid=\"study-tools\"", html, StringComparison.Ordinal);
        Assert.Contains($"/ujian/{exam.Id}/mulai?mode=Simulation", html, StringComparison.Ordinal);
        Assert.Contains($"/learn/target?examId={exam.Id}&tanggal=2027-01-18", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"tool-cards\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"cta-buy\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hidden_and_unknown_tests_answer_404()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var hidden = Test(TestData.Unique(), "hidden", null, published: false);
        await SeedAsync(hidden);

        using var a = await guest.GetAsync(new Uri($"/jadwal-tes/{hidden.Slug}", UriKind.Relative));
        using var b = await guest.GetAsync(new Uri("/jadwal-tes/tidak-ada-" + TestData.Unique(), UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, a.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, b.StatusCode);
    }

    [Fact]
    public async Task An_admin_adds_edits_and_deletes_a_test()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var slug = "tes-" + TestData.Unique();
        var form = new Dictionary<string, string>
        {
            ["Input.Name"] = "Tes Admin",
            ["Input.Slug"] = slug,
            ["Input.Organizer"] = "BKN",
            ["Input.Category"] = nameof(UpcomingTestCategory.Pppk),
            ["Input.ScheduleStatus"] = nameof(ScheduleStatus.Official),
            ["Input.TestStartsOn"] = "2027-02-01",
            ["Input.IsPublished"] = "true",
        };

        using var created = await admin.PostFormAsync("/admin/jadwal-tes/form", form);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        long id;
        await using (var db = database.CreateContext())
        {
            var test = await db.UpcomingTests.SingleAsync(t => t.Slug == slug);
            Assert.Equal((UpcomingTestCategory.Pppk, new DateOnly(2027, 2, 1), true), (test.Category, test.TestStartsOn!.Value, test.IsPublished));
            id = test.Id;
        }

        form["Input.Slug"] = "Bukan Slug";
        using var invalid = await admin.PostFormAsync($"/admin/jadwal-tes/form/{id}", form);
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("data-testid=\"error-slug\"", await invalid.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var deleted = await admin.PostFormAsync($"/admin/jadwal-tes/form/{id}", new Dictionary<string, string>(), $"/admin/jadwal-tes/form/{id}?handler=Delete");
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        await using var check = database.CreateContext();
        Assert.False(await check.UpcomingTests.AnyAsync(t => t.Id == id));
    }

    [Fact]
    public async Task Learners_cannot_open_the_admin_list()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var response = await learner.GetAsync(new Uri("/admin/jadwal-tes", UriKind.Relative));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task SeedAsync(params UpcomingTest[] tests)
    {
        await using var db = database.CreateContext();
        db.UpcomingTests.AddRange(tests);
        await db.SaveChangesAsync();
    }

    private static UpcomingTest Test(string tag, string name, DateOnly? testDay, bool published = true) => new()
    {
        Slug = $"{tag}-{name}",
        Name = $"{tag}-{name}",
        Organizer = "Penyelenggara",
        Category = UpcomingTestCategory.Cpns,
        TestStartsOn = testDay,
        ScheduleStatus = testDay is null ? ScheduleStatus.NotAnnounced : ScheduleStatus.Estimated,
        IsPublished = published,
        CreatedAt = TestData.Now,
        UpdatedAt = TestData.Now,
    };
}
