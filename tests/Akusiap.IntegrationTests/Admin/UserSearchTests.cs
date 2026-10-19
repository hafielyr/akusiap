using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>USR-01: the admin user list (search, combined filters, paging) and the user detail page.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class UserSearchTests(PostgresFixture database)
{
    [Fact]
    public async Task Search_matches_email_or_name_and_filters_combine()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var token = TestData.Unique();
        var teacherSubscribed = await UserAsync(app, $"Guru Satu {token}", RoleNames.Learner, RoleNames.Teacher);
        var teacherPlain = await UserAsync(app, $"Guru Dua {token}", RoleNames.Teacher);
        var learner = await UserAsync(app, $"Murid {token}", RoleNames.Learner);
        var inactive = await UserAsync(app, $"Murid Lama {token}", RoleNames.Learner);
        await SubscribeAsync(teacherSubscribed.Id);
        await using (var db = database.CreateContext())
        {
            var user = await db.Users.SingleAsync(u => u.Id == inactive.Id);
            user.IsActive = false;
            user.DeactivatedAt = TestData.Now;
            await db.SaveChangesAsync();
        }

        Assert.Equal(4, (await IdsAsync(admin, $"q={token.ToUpperInvariant()}")).Count);
        Assert.Equal([learner.Id], await IdsAsync(admin, $"q={Uri.EscapeDataString(learner.Email!)}"));
        Assert.Equal(new[] { teacherSubscribed.Id, teacherPlain.Id }.Order(), (await IdsAsync(admin, $"q={token}&peran=Teacher")).Order());
        Assert.Equal([teacherSubscribed.Id], await IdsAsync(admin, $"q={token}&peran=Teacher&langganan=Active"));
        Assert.Equal([teacherPlain.Id], await IdsAsync(admin, $"q={token}&peran=Teacher&langganan=None"));
        Assert.Equal([inactive.Id], await IdsAsync(admin, $"q={token}&aktif=false"));
        Assert.Empty(await IdsAsync(admin, $"q={token}&peran=Admin"));
    }

    [Fact]
    public async Task Paging_shows_20_per_page_and_keeps_the_filters()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var token = TestData.Unique();
        await using (var db = database.CreateContext())
        {
            for (var i = 0; i < 25; i++)
            {
                db.Users.Add(TestData.User().Named($"Halaman {token} {i:00}").Build());
            }

            await db.SaveChangesAsync();
        }

        var first = await admin.GetStringAsync(new Uri($"/admin/pengguna?q={token}&aktif=true", UriKind.Relative));
        var second = await admin.GetStringAsync(new Uri($"/admin/pengguna?q={token}&aktif=true&hal=2", UriKind.Relative));

        Assert.Equal(20, RowIds(first).Count);
        Assert.Equal(5, RowIds(second).Count);
        Assert.Empty(RowIds(first).Intersect(RowIds(second)));
        Assert.Contains($"href=\"/admin/pengguna?q={token}&amp;aktif=true&amp;hal=2\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Detail_shows_profile_roles_access_and_attempts()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var user = await UserAsync(app, "Sari " + TestData.Unique(), RoleNames.Learner, RoleNames.Teacher);
        await SubscribeAsync(user.Id);
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
            for (var i = 0; i < 2; i++)
            {
                db.Attempts.Add(new Attempt { UserId = user.Id, ExamId = exam.Id, Source = AttemptSource.Exam, Mode = AttemptMode.Practice, RulesSnapshot = "{}", StartedAt = TestData.Now, LastSavedAt = TestData.Now });
            }

            await db.SaveChangesAsync();
        }

        var html = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/pengguna/{user.Id}", UriKind.Relative)));
        using var unknown = await admin.GetAsync(new Uri($"/admin/pengguna/{Guid.NewGuid()}", UriKind.Relative));

        Assert.Contains("Pelajar, Pengajar", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"user-attempts\">2<", html, StringComparison.Ordinal);
        Assert.Contains("Paket Pelajar", html, StringComparison.Ordinal);
        Assert.Contains($"/admin/akses?email={Uri.EscapeDataString(user.Email!)}", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Learners_cannot_list_users()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var response = await learner.GetAsync(new Uri("/admin/pengguna", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Helpers ----------

    private static Task<User> UserAsync(AppFactory app, string name, params string[] roles) =>
        app.CreateUserAsync(TestData.User().Named(name).Build(), "kopi-susu-2026", roles);

    private async Task SubscribeAsync(Guid userId)
    {
        await using var db = database.CreateContext();
        var plan = await db.Plans.SingleAsync(p => p.Code == PlanCode.Learner);
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

    private static async Task<List<Guid>> IdsAsync(HttpClient admin, string query) =>
        RowIds(await admin.GetStringAsync(new Uri($"/admin/pengguna?{query}", UriKind.Relative)));

    private static List<Guid> RowIds(string html) =>
        [.. RowPattern().Matches(html).Select(m => Guid.Parse(m.Groups[1].Value))];

    [GeneratedRegex("data-testid=\"user-row\" data-user-id=\"([0-9a-f-]{36})\"")]
    private static partial Regex RowPattern();
}
