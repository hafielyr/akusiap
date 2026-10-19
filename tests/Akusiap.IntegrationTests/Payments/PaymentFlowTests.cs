using System.Net;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Payments;
using Akusiap.Domain.Access;
using Akusiap.Domain.Payments;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Payments;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>PAY-01: the fake gateway takes a payment from Pending to Paid, and notifications are stored once.</summary>
[Collection(UsesDatabase.Name)]
public sealed class PaymentFlowTests(PostgresFixture database)
{
    [Fact]
    public async Task The_fake_gateway_completes_a_payment_end_to_end_on_the_dev_page()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var client = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedExamAsync();

        using var created = await client.PostFormAsync("/dev/pembayaran", new Dictionary<string, string> { ["examId"] = $"{examId}", ["method"] = "QRIS" }, "/dev/pembayaran?handler=Create");
        var payment = await LatestPaymentAsync(examId);

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal((PaymentStatus.Pending, 49_000L, PaymentMethod.QRIS), (payment.Status, payment.AmountIdr, payment.Method));
        Assert.True(OrderNumbers.IsValid(payment.OrderNumber), payment.OrderNumber);
        Assert.StartsWith("FAKE-QRIS|", payment.QrString, StringComparison.Ordinal);
        Assert.Equal(app.Clock.UtcNow.AddHours(24), payment.ExpiresAt);

        using var paid = await client.PostFormAsync("/dev/pembayaran", new Dictionary<string, string> { ["paymentId"] = $"{payment.Id}", ["status"] = "PAID" }, "/dev/pembayaran?handler=Notify");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/dev/pembayaran", UriKind.Relative)));

        var after = await LatestPaymentAsync(examId);
        Assert.Equal((PaymentStatus.Paid, app.Clock.UtcNow), (after.Status, after.PaidAt!.Value));
        Assert.Contains($"{payment.OrderNumber}: PAID → Applied", html, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        var stored = await db.PaymentWebhookEvents.SingleAsync(e => e.PaymentId == payment.Id);
        Assert.True(stored.SignatureValid);
        Assert.NotNull(stored.ProcessedAt);
    }

    [Fact]
    public async Task Virtual_account_payments_get_a_va_number_and_one_item_has_one_pending_payment()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var (userId, purchaseId) = await PurchaseAsync(app);

        var first = await CreateAsync(app, userId, purchaseId, PaymentMethod.VirtualAccount, "BNI");
        var second = await CreateAsync(app, userId, purchaseId, PaymentMethod.QRIS, null);

        Assert.True(first.Succeeded);
        Assert.Matches("^8808[0-9]{12}$", first.Value!.VaNumber!);
        Assert.Equal("BNI", first.Value.Channel);
        Assert.Equal(first.Value.OrderNumber, first.Value.InvoiceNumber);
        Assert.Equal([CreatePayment.PendingExists], second.Problems.Select(p => p.Code));
    }

    [Fact]
    public async Task Duplicate_unsigned_and_unknown_notifications_are_stored_but_never_applied_twice()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var (userId, purchaseId) = await PurchaseAsync(app);
        var payment = (await CreateAsync(app, userId, purchaseId, PaymentMethod.QRIS, null)).Value!;
        var eventId = Guid.NewGuid().ToString();
        var good = FakePaymentGateway.CreateNotification(payment.InvoiceNumber, "PAID", payment.AmountIdr, eventId);
        var forged = good with { Headers = new Dictionary<string, string> { [FakePaymentGateway.SignatureHeader] = "00" }, Body = good.Body.Replace(eventId, "forged-" + eventId, StringComparison.Ordinal) };

        var rejected = await ApplyAsync(app, forged);
        Assert.Equal(PaymentStatus.Pending, (await LoadAsync(payment.Id)).Status);
        var applied = await ApplyAsync(app, good);
        var duplicate = await ApplyAsync(app, good);
        var unknown = await ApplyAsync(app, FakePaymentGateway.CreateNotification("AKS-19990101-ZZZZ", "PAID", 1));
        var unreadable = await ApplyAsync(app, new GatewayNotification(new Dictionary<string, string>(), "not json"));

        Assert.Equal(
            [NotificationOutcome.InvalidSignature, NotificationOutcome.Applied, NotificationOutcome.Duplicate, NotificationOutcome.UnknownPayment, NotificationOutcome.Unreadable],
            [rejected, applied, duplicate, unknown, unreadable]);
        Assert.Equal(PaymentStatus.Paid, (await LoadAsync(payment.Id)).Status);
        await using var db = database.CreateContext();
        var events = await db.PaymentWebhookEvents.Where(e => e.PaymentId == payment.Id).OrderBy(e => e.Id).ToListAsync();
        Assert.Equal([false, true], events.Select(e => e.SignatureValid));
        Assert.Null(events[0].ProcessedAt);
    }

    [Fact]
    public async Task Expired_notifications_close_a_pending_payment_and_a_later_success_is_only_flagged_as_late()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var (userId, purchaseId) = await PurchaseAsync(app);
        var payment = (await CreateAsync(app, userId, purchaseId, PaymentMethod.QRIS, null)).Value!;

        var expired = await ApplyAsync(app, FakePaymentGateway.CreateNotification(payment.InvoiceNumber, "EXPIRED", payment.AmountIdr));
        var latePaid = await ApplyAsync(app, FakePaymentGateway.CreateNotification(payment.InvoiceNumber, "PAID", payment.AmountIdr));

        Assert.Equal((NotificationOutcome.Applied, NotificationOutcome.LatePayment), (expired, latePaid));
        var after = await LoadAsync(payment.Id);
        Assert.Equal((PaymentStatus.Expired, true), (after.Status, after.IsLatePayment));
    }

    [Fact]
    public async Task The_dev_page_does_not_exist_outside_development()
    {
        await using var app = new AppFactory(database);
        await using var production = app.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var client = production.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.GetAsync(new Uri("/dev/pembayaran", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<long> SeedExamAsync()
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(TestData.Exam().Titled("Bayar " + TestData.Unique()).Paid(49_000).WithQuestions(1, trial: 1).Build())).Id;
    }

    private async Task<(Guid UserId, long PurchaseId)> PurchaseAsync(AppFactory app)
    {
        var user = await app.CreateUserAsync(TestData.User().Build(), "kopi-susu-2026", RoleNames.Learner);
        var examId = await SeedExamAsync();
        await using var db = database.CreateContext();
        var purchase = new ExamPurchase { UserId = user.Id, ExamId = examId, PricePaidIdr = 49_000 };
        db.ExamPurchases.Add(purchase);
        await db.SaveChangesAsync();
        return (user.Id, purchase.Id);
    }

    private static async Task<Akusiap.Application.Common.Result<Payment>> CreateAsync(AppFactory app, Guid userId, long purchaseId, PaymentMethod method, string? channel)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CreatePayment>().HandleAsync(
            new CreatePaymentRequest(userId, PaymentItem.ForExamPurchase(purchaseId, "Tryout"), 49_000, method, channel), CancellationToken.None);
    }

    private static async Task<NotificationOutcome> ApplyAsync(AppFactory app, GatewayNotification notification)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<ApplyGatewayNotification>().HandleAsync(notification, CancellationToken.None)).Outcome;
    }

    private async Task<Payment> LoadAsync(long id)
    {
        await using var db = database.CreateContext();
        return await db.Payments.SingleAsync(p => p.Id == id);
    }

    private async Task<Payment> LatestPaymentAsync(long examId)
    {
        await using var db = database.CreateContext();
        return await db.Payments.Where(p => p.ExamPurchase!.ExamId == examId).OrderByDescending(p => p.Id).FirstAsync();
    }
}
