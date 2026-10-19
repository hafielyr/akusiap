using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Access;

/// <summary>ENT-04: <c>/learn/akses</c> lists the learner's purchases and subscriptions by end date, ended ones apart.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class MyAccessTests(PostgresFixture database)
{
    private static readonly DateTime Now = TestClock.DefaultStart;

    [Fact]
    public async Task Active_items_come_first_by_end_date_and_ended_ones_are_listed_apart()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var in20 = await PurchaseAsync(user.Id, ExamPurchaseStatus.Active, Now.AddDays(20));
        var in5 = await PurchaseAsync(user.Id, ExamPurchaseStatus.Active, Now.AddDays(5));
        var lapsed = await PurchaseAsync(user.Id, ExamPurchaseStatus.Active, Now.AddDays(-2)); // Active, but its end has passed
        var expired = await PurchaseAsync(user.Id, ExamPurchaseStatus.Expired, Now.AddDays(-10));
        var refunded = await PurchaseAsync(user.Id, ExamPurchaseStatus.Refunded, Now.AddDays(-1));
        var unpaid = await PurchaseAsync(user.Id, ExamPurchaseStatus.AwaitingPayment, Now.AddDays(30));
        var subscription = await SubscriptionAsync(user.Id, Now.AddDays(10));

        var html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/learn/akses", UriKind.Relative)));

        Assert.Equal(
            [$"Purchase:{in5}", $"Subscription:{subscription}", $"Purchase:{in20}"],
            Items(Section(html, "active-list")));
        Assert.Equal(
            [$"Purchase:{refunded}", $"Purchase:{lapsed}", $"Purchase:{expired}"],
            Items(Section(html, "ended-list")));
        Assert.DoesNotContain($"data-id=\"{unpaid}\"", html, StringComparison.Ordinal);
        Assert.Contains("5 hari lagi", html, StringComparison.Ordinal);
        Assert.Contains("Semua ujian di katalog", html, StringComparison.Ordinal);
        Assert.Contains("Dikembalikan", html, StringComparison.Ordinal);
        Assert.Contains("WIB", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Active_purchases_link_to_start_practice()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var purchaseId = await PurchaseAsync(user.Id, ExamPurchaseStatus.Active, Now.AddDays(3));
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.ExamPurchases.SingleAsync(p => p.Id == purchaseId)).ExamId;
        }

        var html = await client.GetStringAsync(new Uri("/learn/akses", UriKind.Relative));

        Assert.Contains($"href=\"/ujian/{examId}/mulai\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ujian/{examId}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Learner_without_access_sees_the_empty_state()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var client = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        var html = await client.GetStringAsync(new Uri("/learn/akses", UriKind.Relative));

        Assert.Contains("data-testid=\"access-empty\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Guests_are_sent_to_sign_in()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await guest.GetAsync(new Uri("/learn/akses", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        Assert.StartsWith("/akun/masuk", location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString, StringComparison.Ordinal);
    }

    // ---------- Helpers ----------

    private async Task<long> PurchaseAsync(Guid userId, ExamPurchaseStatus status, DateTime end)
    {
        await using var db = database.CreateContext();
        var exam = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1).Build());
        var purchase = new ExamPurchase
        {
            UserId = userId,
            ExamId = exam.Id,
            PricePaidIdr = 49_000,
            Status = status,
            PurchasedAt = status == ExamPurchaseStatus.AwaitingPayment ? null : Now.AddDays(-30),
            AccessExpiresAt = end,
        };
        db.ExamPurchases.Add(purchase);
        await db.SaveChangesAsync();
        return purchase.Id;
    }

    private async Task<long> SubscriptionAsync(Guid userId, DateTime end)
    {
        await using var db = database.CreateContext();
        var plan = await db.Plans.SingleAsync(p => p.Code == PlanCode.Learner);
        var price = await db.PlanPrices.FirstOrDefaultAsync(p => p.PlanId == plan.Id)
            ?? db.PlanPrices.Add(new PlanPrice { PlanId = plan.Id, DurationMonths = 1, PriceIdr = 99_000 }).Entity;
        var subscription = new Subscription
        {
            UserId = userId,
            PlanId = plan.Id,
            PlanPrice = price,
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = Now.AddDays(-20),
            CurrentPeriodEnd = end,
            CreatedAt = Now.AddDays(-20),
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }

    private static string Section(string html, string testId)
    {
        var start = html.IndexOf($"data-testid=\"{testId}\"", StringComparison.Ordinal);
        Assert.True(start >= 0, $"No {testId} on the page");
        var section = html[start..];
        return section[..section.IndexOf("</ul>", StringComparison.Ordinal)];
    }

    private static List<string> Items(string html) =>
        [.. ItemPattern().Matches(html).Select(m => $"{m.Groups[1].Value}:{m.Groups[2].Value}")];

    [GeneratedRegex("data-testid=\"access-item\" data-kind=\"(\\w+)\" data-id=\"(\\d+)\"")]
    private static partial Regex ItemPattern();
}
