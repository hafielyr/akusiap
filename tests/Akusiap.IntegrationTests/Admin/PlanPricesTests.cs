using System.Net;
using Akusiap.Domain.Access;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>
/// PAY-02: the admin sets plan package prices. Plans are shared reference data, so each test gets its own database
/// instead of the shared one.
/// </summary>
public sealed class PlanPricesTests : IAsyncLifetime
{
    private const string Page = "/admin/paket";
    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Admin_sets_prices_and_saving_again_updates_the_same_package()
    {
        await using var site = await StartAsync();
        var learner = await PlanIdAsync(PlanCode.Learner);

        using var first = await SaveAsync(site, learner, ("1", "99000", true), ("3", "Rp249.000", true), ("6", "", false), ("12", "899000", false));
        using var second = await SaveAsync(site, learner, ("1", "89.000", true), ("3", "249000", false), ("6", "", false), ("12", "899000", false));
        var html = WebUtility.HtmlDecode(await site.Admin.GetStringAsync(new Uri(Page, UriKind.Relative)));

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
        Assert.Equal([(1, 89_000L, true), (3, 249_000L, false), (12, 899_000L, false)], await PricesAsync(learner));
        Assert.Contains("Harga paket disimpan.", html, StringComparison.Ordinal);
        Assert.Contains("value=\"89000\"", html, StringComparison.Ordinal);
        await using var db = _database.CreateContext();
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.Action == "Plan.Prices"));
    }

    [Fact]
    public async Task There_is_only_ever_one_price_per_plan_and_duration()
    {
        await using var site = await StartAsync();
        var teacher = await PlanIdAsync(PlanCode.Teacher);
        using (await SaveAsync(site, teacher, ("1", "59000", true)))
        {
        }

        await using var db = _database.CreateContext();
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO plan_prices (plan_id, duration_months, price_idr, is_active) VALUES ({teacher}, 1, 60000, true)"));

        Assert.Equal("ix_plan_prices_plan_id_duration_months", duplicate.ConstraintName);
        Assert.Single(await PricesAsync(teacher));
    }

    [Fact]
    public async Task Bad_values_are_refused_with_the_duration_and_nothing_changes()
    {
        await using var site = await StartAsync();
        var combined = await PlanIdAsync(PlanCode.Combined);

        using var response = await SaveAsync(site, combined, ("1", "abc", true), ("3", "0", true), ("6", "", true), ("12", "", false));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Harga 1 bulan: tulis rupiah tanpa desimal", html, StringComparison.Ordinal);
        Assert.Empty(await PricesAsync(combined));

        using var range = await SaveAsync(site, combined, ("1", "99000", true), ("3", "0", true), ("6", "", true), ("12", "", false));
        var rangeHtml = WebUtility.HtmlDecode(await range.Content.ReadAsStringAsync());
        Assert.Contains("Harga 3 bulan harus lebih dari 0.", rangeHtml, StringComparison.Ordinal);
        Assert.Contains("Paket 6 bulan perlu harga sebelum dijual.", rangeHtml, StringComparison.Ordinal);
        Assert.Contains("value=\"99000\"", rangeHtml, StringComparison.Ordinal);
        Assert.Empty(await PricesAsync(combined));
    }

    [Fact]
    public async Task A_bought_package_can_be_switched_off_but_not_removed()
    {
        await using var site = await StartAsync();
        var learner = await PlanIdAsync(PlanCode.Learner);
        using (await SaveAsync(site, learner, ("1", "99000", true)))
        {
        }

        var buyer = await site.App.CreateUserAsync(TestData.User().Build(), "kopi-susu-2026", RoleNames.Learner);
        await using (var db = _database.CreateContext())
        {
            var price = await db.PlanPrices.SingleAsync(p => p.PlanId == learner);
            db.Subscriptions.Add(new Subscription { UserId = buyer.Id, PlanId = learner, PlanPriceId = price.Id, CreatedAt = TestData.Now });
            await db.SaveChangesAsync();
        }

        using var remove = await SaveAsync(site, learner, ("1", "", false));
        using var switchOff = await SaveAsync(site, learner, ("1", "99000", false));

        Assert.Contains("sudah dibeli", WebUtility.HtmlDecode(await remove.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, switchOff.StatusCode);
        Assert.Equal([(1, 99_000L, false)], await PricesAsync(learner));
        Assert.Contains("1 langganan", await site.Admin.GetStringAsync(new Uri(Page, UriKind.Relative)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_free_plan_has_no_prices_and_learners_cannot_open_the_page()
    {
        await using var site = await StartAsync();
        var free = await PlanIdAsync(PlanCode.Free);
        using var learner = await site.App.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        var html = await site.Admin.GetStringAsync(new Uri(Page, UriKind.Relative));
        using var freePost = await SaveAsync(site, free, ("1", "10000", true));
        using var forbidden = await learner.GetAsync(new Uri(Page, UriKind.Relative));

        Assert.Equal(3, html.Split("data-testid=\"plan-form\"").Length - 1);
        Assert.DoesNotContain("data-plan=\"Free\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, freePost.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    // ---------- Helpers ----------

    private sealed class Site(AppFactory app, HttpClient admin) : IAsyncDisposable
    {
        public AppFactory App { get; } = app;

        public HttpClient Admin { get; } = admin;

        public async ValueTask DisposeAsync()
        {
            Admin.Dispose();
            await App.DisposeAsync();
        }
    }

    private async Task<Site> StartAsync()
    {
        var app = new AppFactory(_database.ConnectionString);
        await app.SeedReferenceDataAsync();
        return new Site(app, await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin));
    }

    private static Task<HttpResponseMessage> SaveAsync(Site site, long planId, params (string Months, string Price, bool Active)[] packages)
    {
        var fields = new Dictionary<string, string> { ["planId"] = planId.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        for (var i = 0; i < packages.Length; i++)
        {
            fields[$"packages[{i}].DurationMonths"] = packages[i].Months;
            fields[$"packages[{i}].Price"] = packages[i].Price;
            if (packages[i].Active)
            {
                fields[$"packages[{i}].IsActive"] = "true";
            }
        }

        return site.Admin.PostFormAsync(Page, fields);
    }

    private async Task<long> PlanIdAsync(PlanCode code)
    {
        await using var db = _database.CreateContext();
        return await db.Plans.Where(p => p.Code == code).Select(p => p.Id).SingleAsync();
    }

    private async Task<List<(int Months, long Price, bool Active)>> PricesAsync(long planId)
    {
        await using var db = _database.CreateContext();
        return [.. (await db.PlanPrices.Where(p => p.PlanId == planId).OrderBy(p => p.DurationMonths).ToListAsync())
            .Select(p => (p.DurationMonths, p.PriceIdr, p.IsActive))];
    }
}
