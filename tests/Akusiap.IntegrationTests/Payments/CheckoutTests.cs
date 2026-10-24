using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Domain;
using Akusiap.Domain.Access;
using Akusiap.Domain.Payments;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>PAY-03: checkout. Each test gets its own database because plan prices and channel settings are shared data.</summary>
public sealed partial class CheckoutTests : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Buying_an_exam_opens_one_pending_payment_and_returning_finds_it()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await ExamAsync(79_000, 30);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/checkout?exam={examId}", UriKind.Relative)));
        using var placed = await client.PostFormAsync($"/checkout?exam={examId}", Order("QRIS"));
        var again = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/checkout?exam={examId}", UriKind.Relative)));
        using var placedAgain = await client.PostFormAsync($"/checkout?exam={examId}", Order("VA-BCA"));

        Assert.Contains("Rp79.000", page, StringComparison.Ordinal);
        Assert.Contains($"Akses sampai {Wib(TestClock.DefaultStart.AddDays(30))}.", page, StringComparison.Ordinal);
        Assert.Equal(["QRIS", "VA-BCA", "VA-BNI", "VA-BRI", "VA-MANDIRI", "EWALLET-OVO", "EWALLET-DANA", "EWALLET-SHOPEEPAY", "CARD"], Channels(page));
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        var order = placed.Headers.Location!.OriginalString["/bayar/".Length..];
        Assert.True(OrderNumbers.IsValid(order), order);
        Assert.Equal($"/bayar/{order}", placedAgain.Headers.Location!.OriginalString);
        Assert.Contains($"Anda sudah punya pembayaran yang menunggu ({order}).", again, StringComparison.Ordinal);

        await using var db = _database.CreateContext();
        var payment = await db.Payments.Include(p => p.ExamPurchase).SingleAsync(p => p.UserId == user.Id);
        Assert.Equal((PaymentStatus.Pending, PaymentMethod.QRIS, 79_000L), (payment.Status, payment.Method, payment.AmountIdr));
        Assert.Equal((ExamPurchaseStatus.AwaitingPayment, examId), (payment.ExamPurchase!.Status, payment.ExamPurchase.ExamId));
    }

    [Fact]
    public async Task Buying_again_extends_running_access_from_its_end()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await ExamAsync(49_000, 30);
        await using (var db = _database.CreateContext())
        {
            db.ExamPurchases.Add(new ExamPurchase
            {
                UserId = user.Id, ExamId = examId, PricePaidIdr = 49_000, Status = ExamPurchaseStatus.Active,
                PurchasedAt = TestClock.DefaultStart.AddDays(-20), AccessExpiresAt = TestClock.DefaultStart.AddDays(10),
            });
            await db.SaveChangesAsync();
        }

        var page = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/checkout?exam={examId}", UriKind.Relative)));
        using var placed = await client.PostFormAsync($"/checkout?exam={examId}", Order("VA-BNI"));

        Assert.Contains($"diperpanjang sampai {Wib(TestClock.DefaultStart.AddDays(40))}", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        await using var check = _database.CreateContext();
        Assert.Equal(1, await check.ExamPurchases.CountAsync(p => p.UserId == user.Id));
        Assert.Equal(ExamPurchaseStatus.Active, (await check.ExamPurchases.SingleAsync(p => p.UserId == user.Id)).Status);
        Assert.NotNull((await check.Payments.SingleAsync(p => p.UserId == user.Id)).VaNumber);
    }

    [Fact]
    public async Task Buying_a_package_creates_an_awaiting_subscription_for_that_package()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long priceId;
        await using (var db = _database.CreateContext())
        {
            var plan = await db.Plans.SingleAsync(p => p.Code == PlanCode.Learner);
            var price = new PlanPrice { PlanId = plan.Id, DurationMonths = 3, PriceIdr = 249_000 };
            db.PlanPrices.Add(price);
            await db.SaveChangesAsync();
            priceId = price.Id;
        }

        var page = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri($"/checkout?planPrice={priceId}", UriKind.Relative)));
        using var placed = await client.PostFormAsync($"/checkout?planPrice={priceId}", Order("EWALLET-OVO"));

        Assert.Contains("Paket Pelajar · 3 bulan", page, StringComparison.Ordinal);
        Assert.Contains($"Akses sampai {Wib(TestClock.DefaultStart.AddMonths(3))}.", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        await using var check = _database.CreateContext();
        var subscription = await check.Subscriptions.SingleAsync(s => s.UserId == user.Id);
        Assert.Equal((SubscriptionStatus.AwaitingPayment, priceId), (subscription.Status, subscription.PlanPriceId));
        var payment = await check.Payments.SingleAsync(p => p.SubscriptionId == subscription.Id);
        Assert.Equal((PaymentMethod.EWallet, "OVO", 249_000L), (payment.Method, payment.Channel!, payment.AmountIdr));
    }

    [Fact]
    public async Task Disabled_channels_and_channels_below_their_minimum_are_hidden_and_refused()
    {
        await using var app = await StartAsync();
        await using var configured = app.WithWebHostBuilder(b => b
            .UseSetting("Payments:Channels:0:Code", "QRIS").UseSetting("Payments:Channels:0:Method", "QRIS").UseSetting("Payments:Channels:0:Label", "QRIS")
            .UseSetting("Payments:Channels:1:Code", "VA-BCA").UseSetting("Payments:Channels:1:Method", "VirtualAccount").UseSetting("Payments:Channels:1:Channel", "BCA")
            .UseSetting("Payments:Channels:1:Label", "BCA").UseSetting("Payments:Channels:1:MinAmountIdr", "100000")
            .UseSetting("Payments:Channels:2:Code", "EWALLET-OVO").UseSetting("Payments:Channels:2:Method", "EWallet").UseSetting("Payments:Channels:2:Channel", "OVO")
            .UseSetting("Payments:Channels:2:Label", "OVO").UseSetting("Payments:Channels:2:Enabled", "false"));
        using var client = await configured.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await ExamAsync(49_000, 30);

        var page = await client.GetStringAsync(new Uri($"/checkout?exam={examId}", UriKind.Relative));
        using var refused = await client.PostFormAsync($"/checkout?exam={examId}", Order("VA-BCA"));

        Assert.Equal(["QRIS"], Channels(page));
        Assert.Contains("Pilih metode pembayaran.", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unconfirmed_user_is_asked_to_confirm_first_and_cannot_pay()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Unconfirmed().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await ExamAsync(49_000, 30);

        var page = await client.GetStringAsync(new Uri($"/checkout?exam={examId}", UriKind.Relative));
        using var refused = await client.PostFormAsync($"/checkout?exam={examId}", Order("QRIS"), $"/checkout?exam={examId}");

        Assert.Contains("data-testid=\"confirm-email-first\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"checkout-form\"", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        await using var db = _database.CreateContext();
        Assert.False(await db.Payments.AnyAsync(p => p.UserId == user.Id));
    }

    [Fact]
    public async Task Terms_must_be_accepted_and_only_buyable_items_have_a_checkout()
    {
        await using var app = await StartAsync();
        using var client = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var examId = await ExamAsync(49_000, 30);
        long free;
        await using (var db = _database.CreateContext())
        {
            free = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build())).Id;
        }

        var form = Order("QRIS");
        form.Remove("AcceptTerms");
        using var noTerms = await client.PostFormAsync($"/checkout?exam={examId}", form);
        using var freeExam = await client.GetAsync(new Uri($"/checkout?exam={free}", UriKind.Relative));
        using var nothing = await client.GetAsync(new Uri("/checkout", UriKind.Relative));
        using var forGuest = await guest.GetAsync(new Uri($"/checkout?exam={examId}", UriKind.Relative));

        Assert.Contains("Centang persetujuan syarat layanan dan kebijakan refund.", WebUtility.HtmlDecode(await noTerms.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, freeExam.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, nothing.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, forGuest.StatusCode);
        Assert.StartsWith("/akun/masuk", forGuest.Headers.Location!.IsAbsoluteUri ? forGuest.Headers.Location.PathAndQuery : forGuest.Headers.Location.OriginalString, StringComparison.Ordinal);
    }

    // ---------- Helpers ----------

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(_database.ConnectionString);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private async Task<long> ExamAsync(long price, int days)
    {
        await using var db = _database.CreateContext();
        return (await db.SaveAsync(TestData.Exam().Paid(price, days).WithQuestions(1, trial: 1).Build())).Id;
    }

    /// <summary>The date as the Indonesian page shows it.</summary>
    private static string Wib(DateTime utc) =>
        JakartaTime.ToLocal(utc).ToString("d MMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("id-ID"));

    private static Dictionary<string, string> Order(string channel) => new()
    {
        ["Channel"] = channel,
        ["AcceptTerms"] = "true",
    };

    private static List<string> Channels(string html) =>
        [.. ChannelPattern().Matches(html).Select(m => m.Groups[1].Value)];

    [GeneratedRegex("data-testid=\"channel\" data-code=\"([A-Z-]+)\"")]
    private static partial Regex ChannelPattern();
}
