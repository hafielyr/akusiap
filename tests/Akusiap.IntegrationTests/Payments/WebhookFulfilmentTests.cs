using System.Net;
using System.Text;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Payments;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Payments;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>
/// PAY-06: gateway notifications on <c>/webhooks/doku</c> fulfil a paid purchase once. Each test gets its own database
/// (plan prices are shared data).
/// </summary>
public sealed class WebhookFulfilmentTests : IAsyncLifetime
{
    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task A_paid_exam_is_unlocked_on_the_next_request_and_a_repeated_event_fulfils_once()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await ExamAsync(30);
        var payment = await CheckoutAsync(learner, $"exam={examId}", user.Id);
        Assert.Equal("cta-trial", await FirstCtaAsync(learner, examId));

        var notification = FakePaymentGateway.CreateNotification(payment.InvoiceNumber, "PAID", payment.AmountIdr);
        using var first = await PostAsync(app, notification);
        using var repeated = await PostAsync(app, notification);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (first.StatusCode, repeated.StatusCode));
        Assert.Equal("cta-start", await FirstCtaAsync(learner, examId));
        await using var db = _database.CreateContext();
        var purchase = await db.ExamPurchases.SingleAsync(p => p.UserId == user.Id);
        Assert.Equal((ExamPurchaseStatus.Active, TestClock.DefaultStart.AddDays(30), 49_000L), (purchase.Status, purchase.AccessExpiresAt!.Value, purchase.PricePaidIdr));
        Assert.Equal(PaymentStatus.Paid, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).Status);
        Assert.Equal(1, await db.PaymentWebhookEvents.CountAsync(e => e.PaymentId == payment.Id));
    }

    [Fact]
    public async Task Buying_again_extends_from_the_running_end()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await ExamAsync(30);
        await using (var db = _database.CreateContext())
        {
            db.ExamPurchases.Add(new ExamPurchase
            {
                UserId = user.Id, ExamId = examId, PricePaidIdr = 49_000, Status = ExamPurchaseStatus.Active,
                PurchasedAt = TestClock.DefaultStart.AddDays(-20), AccessExpiresAt = TestClock.DefaultStart.AddDays(10),
            });
            await db.SaveChangesAsync();
        }

        var payment = await CheckoutAsync(learner, $"exam={examId}", user.Id);
        using (await PostAsync(app, FakePaymentGateway.CreateNotification(payment.InvoiceNumber, "PAID", payment.AmountIdr)))
        {
        }

        await using var check = _database.CreateContext();
        var purchase = await check.ExamPurchases.SingleAsync(p => p.UserId == user.Id);
        Assert.Equal(TestClock.DefaultStart.AddDays(40), purchase.AccessExpiresAt);
        Assert.Equal(TestClock.DefaultStart.AddDays(-20), purchase.PurchasedAt);
    }

    [Fact]
    public async Task A_paid_package_activates_the_subscription_for_its_months()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long priceId;
        await using (var db = _database.CreateContext())
        {
            var plan = await db.Plans.SingleAsync(p => p.Code == PlanCode.Learner);
            var price = new PlanPrice { PlanId = plan.Id, DurationMonths = 3, PriceIdr = 249_000 };
            db.PlanPrices.Add(price);
            await db.SaveChangesAsync();
            priceId = price.Id;
        }

        var payment = await CheckoutAsync(learner, $"planPrice={priceId}", user.Id);
        using (await PostAsync(app, FakePaymentGateway.CreateNotification(payment.InvoiceNumber, "PAID", payment.AmountIdr)))
        {
        }

        await using var check = _database.CreateContext();
        var subscription = await check.Subscriptions.SingleAsync(s => s.UserId == user.Id);
        Assert.Equal((SubscriptionStatus.Active, TestClock.DefaultStart, TestClock.DefaultStart.AddMonths(3)), (subscription.Status, subscription.CurrentPeriodStart!.Value, subscription.CurrentPeriodEnd!.Value));
        Assert.Equal("cta-start", await FirstCtaAsync(learner, await ExamAsync(30)));
    }

    [Fact]
    public async Task Bad_signatures_are_stored_but_never_processed()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await ExamAsync(30);
        var payment = await CheckoutAsync(learner, $"exam={examId}", user.Id);
        var real = FakePaymentGateway.CreateNotification(payment.InvoiceNumber, "PAID", payment.AmountIdr);
        var forged = real with { Headers = new Dictionary<string, string> { [FakePaymentGateway.SignatureHeader] = "forged" } };

        using var response = await PostAsync(app, forged);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("cta-trial", await FirstCtaAsync(learner, examId));
        await using var db = _database.CreateContext();
        var stored = await db.PaymentWebhookEvents.SingleAsync(e => e.PaymentId == payment.Id);
        Assert.False(stored.SignatureValid);
        Assert.Null(stored.ProcessedAt);
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).Status);
    }

    [Fact]
    public async Task An_expired_payment_unlocks_nothing_and_a_late_success_is_only_flagged()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await ExamAsync(30);
        var payment = await CheckoutAsync(learner, $"exam={examId}", user.Id);

        using (await PostAsync(app, FakePaymentGateway.CreateNotification(payment.InvoiceNumber, "EXPIRED", payment.AmountIdr)))
        using (await PostAsync(app, FakePaymentGateway.CreateNotification(payment.InvoiceNumber, "PAID", payment.AmountIdr)))
        {
        }

        Assert.Equal("cta-trial", await FirstCtaAsync(learner, examId));
        await using var db = _database.CreateContext();
        var after = await db.Payments.SingleAsync(p => p.Id == payment.Id);
        Assert.Equal((PaymentStatus.Expired, true), (after.Status, after.IsLatePayment));
        Assert.Equal(ExamPurchaseStatus.AwaitingPayment, (await db.ExamPurchases.SingleAsync(p => p.UserId == user.Id)).Status);
    }

    [Fact]
    public async Task Unreadable_bodies_get_400()
    {
        await using var app = await StartAsync();

        using var response = await PostAsync(app, new GatewayNotification(new Dictionary<string, string>(), "not json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(_database.ConnectionString);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private async Task<long> ExamAsync(int days)
    {
        await using var db = _database.CreateContext();
        return (await db.SaveAsync(TestData.Exam().Paid(49_000, days).WithQuestions(1, trial: 1).Build())).Id;
    }

    private async Task<Payment> CheckoutAsync(HttpClient client, string query, Guid userId)
    {
        using var placed = await client.PostFormAsync($"/checkout?{query}", new Dictionary<string, string> { ["Channel"] = "QRIS", ["AcceptTerms"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        await using var db = _database.CreateContext();
        return await db.Payments.OrderByDescending(p => p.Id).FirstAsync(p => p.UserId == userId);
    }

    private static async Task<HttpResponseMessage> PostAsync(AppFactory app, GatewayNotification notification)
    {
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/doku", UriKind.Relative))
        {
            Content = new StringContent(notification.Body, Encoding.UTF8, "application/json"),
        };
        foreach (var header in notification.Headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return await client.SendAsync(request);
    }

    private static async Task<string?> FirstCtaAsync(HttpClient client, long examId)
    {
        var html = await client.GetStringAsync(new Uri($"/ujian/{examId}", UriKind.Relative));
        var match = System.Text.RegularExpressions.Regex.Match(html, "data-testid=\"(cta-[a-z]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }
}
