using System.Net;
using Akusiap.Application.Catalog;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Catalog;

/// <summary>CAT-03: the pricing page. Plans are shared reference data, so each test gets its own database.</summary>
public sealed class PricingTests : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Packages_show_rupiah_prices_with_monthly_equivalents_and_the_cheapest_per_month_is_highlighted()
    {
        await using var app = await StartAsync();
        await SetPricesAsync((1, 99_000, true), (3, 249_000, true), (6, 449_000, true), (12, 799_000, false));
        using var client = app.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/harga", UriKind.Relative)));

        Assert.Equal(3, html.Split("data-testid=\"package\"").Length - 1);
        Assert.DoesNotContain("data-months=\"12\"", html, StringComparison.Ordinal);
        Assert.Contains("Rp249.000", html, StringComparison.Ordinal);
        Assert.Contains("≈ Rp83.000 per bulan", html, StringComparison.Ordinal);
        Assert.Contains("≈ Rp74.833 per bulan", html, StringComparison.Ordinal);
        Assert.Contains("data-months=\"6\" data-cheapest=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-months=\"1\" data-cheapest=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("Paket Pelajar", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task With_an_exam_the_page_shows_that_exam_and_otherwise_the_lowest_price()
    {
        await using var app = await StartAsync();
        long exam;
        await using (var db = _database.CreateContext())
        {
            exam = (await db.SaveAsync(TestData.Exam().Titled("Tryout CPNS").Paid(79_000, accessDays: 45).WithQuestions(1, trial: 1).Build())).Id;
            await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1).Build());
            await db.SaveAsync(TestData.Exam().Paid(9_000).Draft().WithQuestions(1, trial: 1).Build());
        }

        using var client = app.CreateClient();
        var withExam = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/harga?ujian={exam}", UriKind.Relative)));
        var general = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/harga", UriKind.Relative)));

        Assert.Contains("Tryout CPNS", withExam, StringComparison.Ordinal);
        Assert.Contains("Rp79.000", withExam, StringComparison.Ordinal);
        Assert.Contains("45 hari akses", withExam, StringComparison.Ordinal);
        Assert.Contains($"href=\"/checkout?exam={exam}\"", withExam, StringComparison.Ordinal);
        Assert.Contains("Mulai dari", general, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"exam-price\">Rp49.000<", general, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"no-packages\"", general, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(99_000, 1, 99_000)]
    [InlineData(249_000, 3, 83_000)]
    [InlineData(449_000, 6, 74_833)]
    [InlineData(100, 3, 33)]
    [InlineData(5, 2, 3)] // 2.5 rounds up
    public void Monthly_equivalent_is_rounded_to_whole_rupiah(long price, int months, long monthly)
    {
        Assert.Equal(monthly, GetPricing.MonthlyEquivalent(price, months));
    }

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(_database.ConnectionString);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private async Task SetPricesAsync(params (int Months, long Price, bool Active)[] prices)
    {
        await using var db = _database.CreateContext();
        var plan = await db.Plans.SingleAsync(p => p.Code == PlanCode.Learner);
        foreach (var (months, price, active) in prices)
        {
            db.PlanPrices.Add(new PlanPrice { PlanId = plan.Id, DurationMonths = months, PriceIdr = price, IsActive = active });
        }

        await db.SaveChangesAsync();
    }
}
