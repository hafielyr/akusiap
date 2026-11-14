using System.Net;
using Akusiap.Domain.Access;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Accounts;

/// <summary>PAY-13 (D-141): a subscription covers 1 device plus its extras; the newest sign-in pushes out the oldest.</summary>
[Collection(UsesDatabase.Name)]
public sealed class DeviceLimitTests(PostgresFixture database)
{
    private const string Password = "test-sandi-2026";

    [Fact]
    public async Task A_subscriber_signing_in_on_a_second_device_signs_out_the_first_with_a_notice()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        await SubscribeAsync(user.Id, extraDevices: 0, app.Clock.UtcNow);
        using var phone = await SignInAsync(app, user);
        using var laptop = await SignInAsync(app, user);

        using var pushedOut = await phone.GetAsync(new Uri("/learn", UriKind.Relative));
        var notice = WebUtility.HtmlDecode(await phone.GetStringAsync(new Uri("/akun/masuk", UriKind.Relative)));
        var noticeAgain = await phone.GetStringAsync(new Uri("/akun/masuk", UriKind.Relative));
        using var stillIn = await laptop.GetAsync(new Uri("/learn", UriKind.Relative));
        var profile = WebUtility.HtmlDecode(await laptop.GetStringAsync(new Uri("/learn/profil", UriKind.Relative)));

        Assert.Equal(HttpStatusCode.Redirect, pushedOut.StatusCode);
        Assert.StartsWith("/akun/masuk", pushedOut.Headers.Location!.IsAbsoluteUri ? pushedOut.Headers.Location.PathAndQuery : pushedOut.Headers.Location.OriginalString, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"pushed-out\"", notice, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"pushed-out\"", noticeAgain, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, stillIn.StatusCode);
        Assert.Contains("berlaku untuk 1 perangkat", profile, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(profile, "data-testid=\"device-row\""));
    }

    [Fact]
    public async Task Extra_devices_stay_signed_in_until_the_limit_and_then_the_oldest_goes()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        await SubscribeAsync(user.Id, extraDevices: 1, app.Clock.UtcNow);
        using var first = await SignInAsync(app, user);
        app.Clock.Advance(TimeSpan.FromMinutes(1));
        using var second = await SignInAsync(app, user);

        Assert.Equal(HttpStatusCode.OK, (await first.GetAsync(new Uri("/learn", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync(new Uri("/learn", UriKind.Relative))).StatusCode);

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        using var third = await SignInAsync(app, user);

        Assert.Equal(HttpStatusCode.Redirect, (await first.GetAsync(new Uri("/learn", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await second.GetAsync(new Uri("/learn", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await third.GetAsync(new Uri("/learn", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Accounts_without_a_subscription_and_signing_in_again_on_the_same_device_are_not_limited()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var free = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        using var a = await SignInAsync(app, free);
        using var b = await SignInAsync(app, free);
        var subscriber = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        await SubscribeAsync(subscriber.Id, extraDevices: 0, app.Clock.UtcNow);
        using var device = await SignInAsync(app, subscriber);
        using (await device.PostFormAsync("/learn/profil", new Dictionary<string, string>(), "/akun/keluar"))
        {
        }

        using (await device.PostFormAsync("/akun/masuk", new Dictionary<string, string> { ["Input.Email"] = subscriber.Email!, ["Input.Password"] = Password }))
        {
        }

        Assert.Equal(HttpStatusCode.OK, (await a.GetAsync(new Uri("/learn", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await b.GetAsync(new Uri("/learn", UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await device.GetAsync(new Uri("/learn", UriKind.Relative))).StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.UserDevices.CountAsync(d => d.UserId == subscriber.Id));
    }

    private static async Task<HttpClient> SignInAsync(AppFactory app, User user)
    {
        var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 Chrome/130.0 Mobile Safari/537.36");
        using var response = await client.PostFormAsync("/akun/masuk", new Dictionary<string, string> { ["Input.Email"] = user.Email!, ["Input.Password"] = Password });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task SubscribeAsync(Guid userId, int extraDevices, DateTime now)
    {
        await using var db = database.CreateContext();
        var plan = await db.Plans.SingleAsync(p => p.Code == PlanCode.Learner);
        var price = await db.PlanPrices.FirstOrDefaultAsync(p => p.PlanId == plan.Id && p.DurationMonths == 1);
        if (price is null)
        {
            price = new PlanPrice { PlanId = plan.Id, DurationMonths = 1, PriceIdr = 20_000, IsActive = true };
            db.PlanPrices.Add(price);
            await db.SaveChangesAsync();
        }

        db.Subscriptions.Add(new Subscription
        {
            UserId = userId, PlanId = plan.Id, PlanPriceId = price.Id, Status = SubscriptionStatus.Active, CreatedAt = now,
            CurrentPeriodStart = now.AddDays(-1), CurrentPeriodEnd = now.AddDays(29), ExtraDevices = extraDevices,
        });
        await db.SaveChangesAsync();
    }
}
