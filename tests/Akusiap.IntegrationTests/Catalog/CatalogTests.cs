using System.Data.Common;
using System.Net;
using Akusiap.Application.Access;
using Akusiap.Application.Catalog;
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

namespace Akusiap.IntegrationTests.Catalog;

/// <summary>
/// CAT-01: the public catalog. The shared test database holds other tests' exams, so each test tags its exams with a
/// unique word and searches for it.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed class CatalogTests(PostgresFixture database)
{
    [Fact]
    public async Task Draft_unpublished_taken_down_and_learner_exams_never_appear()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();
        var token = TestData.Unique();
        var owner = await SaveAsync(TestData.User().Build());
        var published = await SeedAsync(TestData.Exam().Titled($"Terbit {token}").Paid(49_000).WithQuestions(3, trial: 3));
        var draft = await SeedAsync(TestData.Exam().Titled($"Draf {token}").Draft().WithQuestions(3));
        var unpublished = await SeedAsync(TestData.Exam().Titled($"Batal {token}").WithStatus(ExamStatus.Unpublished).WithQuestions(3));
        var takenDown = await SeedAsync(TestData.Exam().Titled($"Turun {token}").WithStatus(ExamStatus.TakenDown).WithQuestions(3));
        var learnerExam = await SeedAsync(TestData.Exam().Titled($"Milik {token}").OwnedBy(owner).WithQuestions(3));

        var html = await client.GetStringAsync(new Uri($"/ujian?q={token}", UriKind.Relative));

        Assert.Equal(new[] { published }, CardIds(html));
        Assert.DoesNotContain($"data-exam-id=\"{draft}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-exam-id=\"{unpublished}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-exam-id=\"{takenDown}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-exam-id=\"{learnerExam}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_works_without_javascript_on_title_and_description()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();
        var token = TestData.Unique();
        var byTitle = await SeedAsync(TestData.Exam().Titled($"Tryout {token} CPNS").WithQuestions(2), description: "Latihan SKD.");
        var byDescription = await SeedAsync(TestData.Exam().Titled("Tryout UTBK").WithQuestions(2), description: $"Soal penalaran {token.ToUpperInvariant()} terbaru.");

        var html = await client.GetStringAsync(new Uri($"/ujian?q={token}", UriKind.Relative));

        Assert.Contains("<html", html, StringComparison.Ordinal);
        Assert.Contains($"value=\"{token}\"", html, StringComparison.Ordinal);
        Assert.Equal(new[] { byDescription, byTitle }.Order(), CardIds(html).Order());
    }

    [Fact]
    public async Task Htmx_requests_get_only_the_results_fragment()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();
        var token = TestData.Unique();
        await SeedAsync(TestData.Exam().Titled($"Fragmen {token}").WithQuestions(2));
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri($"/ujian?q={token}", UriKind.Relative));
        request.Headers.Add("HX-Request", "true");

        using var response = await client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.StartsWith("<div id=\"catalog-results\"", html.TrimStart(), StringComparison.Ordinal);
        Assert.DoesNotContain("<html", html, StringComparison.Ordinal);
        Assert.Single(CardIds(html));
    }

    [Fact]
    public async Task Subject_filter_narrows_the_list()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();
        var token = TestData.Unique();
        var first = await SeedAsync(TestData.Exam().Titled($"Satu {token}").WithQuestions(2));
        await SeedAsync(TestData.Exam().Titled($"Dua {token}").WithQuestions(2));
        long subjectId;
        await using (var db = database.CreateContext())
        {
            subjectId = (await db.Exams.SingleAsync(e => e.Id == first)).SubjectId!.Value;
        }

        var html = await client.GetStringAsync(new Uri($"/ujian?q={token}&subjek={subjectId}", UriKind.Relative));

        Assert.Equal(new[] { first }, CardIds(html));
    }

    [Fact]
    public async Task Cards_show_price_access_period_active_question_count_and_topics()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();
        var token = TestData.Unique();
        var longDescription = $"{token} " + string.Join(' ', Enumerable.Repeat("latihan", 40));
        long examId;
        await using (var db = database.CreateContext())
        {
            var exam = TestData.Exam().Titled($"Kartu {token}").Paid(49_000, accessDays: 30).WithQuestions(5, trial: 5).Build();
            exam.Description = longDescription;
            exam.Questions.First().Question!.Status = QuestionStatus.Retired;
            examId = (await db.SaveAsync(exam)).Id;
        }

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/ujian?q={token}", UriKind.Relative)));

        Assert.Single(CardIds(html));
        Assert.Contains("Rp49.000", html, StringComparison.Ordinal);
        Assert.Contains("30 hari akses", html, StringComparison.Ordinal);
        Assert.Contains("4 soal · Topik ", html, StringComparison.Ordinal);
        Assert.Contains("…", html, StringComparison.Ordinal);
        Assert.DoesNotContain(longDescription, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Badges_show_gratis_price_or_akses_anda_for_the_viewer()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var token = TestData.Unique();
        var free = await SeedAsync(TestData.Exam().Titled($"Gratis {token}").Free().WithQuestions(2));
        var paid = await SeedAsync(TestData.Exam().Titled($"Bayar {token}").Paid(59_000).WithQuestions(2, trial: 2));
        var user = TestData.User().Build();
        using var buyer = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        await using (var db = database.CreateContext())
        {
            db.ExamPurchases.Add(new ExamPurchase
            {
                UserId = user.Id,
                ExamId = paid,
                PricePaidIdr = 59_000,
                Status = ExamPurchaseStatus.Active,
                PurchasedAt = TestData.Now,
                AccessExpiresAt = app.Clock.UtcNow.AddDays(30),
            });
            await db.SaveChangesAsync();
        }

        using var guest = app.CreateClient();
        var guestHtml = await guest.GetStringAsync(new Uri($"/ujian?q={token}", UriKind.Relative));
        var buyerHtml = await buyer.GetStringAsync(new Uri($"/ujian?q={token}", UriKind.Relative));

        Assert.Equal("Gratis", BadgeOf(guestHtml, free));
        Assert.Equal("Rp59.000", BadgeOf(guestHtml, paid));
        Assert.Equal("Akses Anda", BadgeOf(buyerHtml, paid));
        Assert.Equal("Gratis", BadgeOf(buyerHtml, free));
    }

    [Fact]
    public async Task Catalog_pages_by_20_within_the_html_budget_and_without_images()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient();
        var token = TestData.Unique();
        await using (var db = database.CreateContext())
        {
            for (var i = 1; i <= 25; i++)
            {
                var exam = TestData.Exam().Titled($"Halaman {token} {i:00}").Paid(49_000).WithQuestions(4, trial: 4).Build();
                exam.Description = string.Join(' ', Enumerable.Repeat("Deskripsi ujian yang cukup panjang untuk kartu.", 6));
                db.Exams.Add(exam);
            }

            await db.SaveChangesAsync();
        }

        var first = await client.GetStringAsync(new Uri($"/ujian?q={token}", UriKind.Relative));
        var second = await client.GetStringAsync(new Uri($"/ujian?q={token}&hal=2", UriKind.Relative));

        Assert.Equal(20, CardIds(first).Count);
        Assert.Equal(5, CardIds(second).Count);
        Assert.Contains("Halaman 2 dari 2", second, StringComparison.Ordinal);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(first) < 50 * 1024, $"First page is {System.Text.Encoding.UTF8.GetByteCount(first)} bytes");
        Assert.DoesNotContain("<img", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_page_of_cards_costs_the_same_number_of_queries_however_many_cards_it_has()
    {
        var token = TestData.Unique();
        await using (var db = database.CreateContext())
        {
            for (var i = 0; i < 20; i++)
            {
                db.Exams.Add(TestData.Exam().Titled($"Kueri {token} {i}").Paid(49_000).WithQuestions(3, trial: 3).Build());
            }

            await db.SaveChangesAsync();
        }

        var user = await SaveAsync(TestData.User().Build());
        var counter = new CommandCounter();
        await using var counted = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(database.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(counter).Options);
        var catalog = new ListCatalog(counted, new EntitlementService(counted, new FakeCurrentUser(user.Id), new TestClock()));

        var page = await catalog.HandleAsync(new CatalogFilter(token), CancellationToken.None);

        Assert.Equal(20, page.Items.Count);
        // Count + page of cards + the viewer's purchases/subscriptions; nothing per card.
        Assert.Equal(3, counter.Count);
    }

    [Theory]
    [InlineData(true, AccessLevel.Full, CatalogBadge.Free)]
    [InlineData(false, AccessLevel.Full, CatalogBadge.YourAccess)]
    [InlineData(false, AccessLevel.Admin, CatalogBadge.YourAccess)]
    [InlineData(false, AccessLevel.Trial, CatalogBadge.Price)]
    [InlineData(false, AccessLevel.None, CatalogBadge.Price)]
    public void Badge_rules(bool isFree, AccessLevel level, CatalogBadge expected)
    {
        var decision = new AccessDecision(level, AccessReason.NoAccess, level == AccessLevel.None ? QuestionScope.None : QuestionScope.All);

        Assert.Equal(expected, ListCatalog.BadgeFor(isFree, decision));
    }

    // ---------- Helpers ----------

    private async Task<long> SeedAsync(Exam exam, string? description = null)
    {
        exam.Description = description ?? exam.Description;
        await using var db = database.CreateContext();
        return (await db.SaveAsync(exam)).Id;
    }

    private async Task<User> SaveAsync(User user)
    {
        await using var db = database.CreateContext();
        return await db.SaveAsync(user);
    }

    private static List<long> CardIds(string html) =>
        [.. System.Text.RegularExpressions.Regex.Matches(html, "data-testid=\"catalog-card\" data-exam-id=\"(\\d+)\"")
            .Select(m => long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];

    private static string BadgeOf(string html, long examId)
    {
        var card = html.IndexOf($"data-exam-id=\"{examId}\"", StringComparison.Ordinal);
        var badge = html.IndexOf("data-testid=\"badge\">", card, StringComparison.Ordinal) + "data-testid=\"badge\">".Length;
        return WebUtility.HtmlDecode(html[badge..html.IndexOf("</span>", badge, StringComparison.Ordinal)]).Trim();
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
