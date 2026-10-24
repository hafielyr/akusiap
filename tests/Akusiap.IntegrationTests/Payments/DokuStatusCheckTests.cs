using System.Net;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Payments;
using Akusiap.Domain.Access;
using Akusiap.Domain.Payments;
using Akusiap.Infrastructure.Payments;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>
/// PAY-04 fallback (D-111): when DOKU's notification never arrives, the payment page's status check asks DOKU's status
/// API and a SUCCESS pays and unlocks the purchase the same way a webhook does, once; checks are spaced out.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed class DokuStatusCheckTests(PostgresFixture database)
{
    private static readonly DokuOptions Doku = new() { ClientId = "BRN-0000-1234567890123", SecretKey = "SK-test-secret" };

    [Fact]
    public async Task A_success_from_the_status_api_pays_and_unlocks_the_exam_once()
    {
        await using var app = new AppFactory(database);
        var (purchaseId, payment) = await PendingAsync(app);
        var handler = new DokuGatewayTests.StubHandler(HttpStatusCode.OK, Status(payment.InvoiceNumber, "SUCCESS"));
        using var scope = app.Services.CreateScope();
        var check = Check(app, scope, handler);

        var changed = await check.HandleAsync(payment.Id, CancellationToken.None);

        Assert.True(changed);
        await using var db = database.CreateContext();
        Assert.Equal(PaymentStatus.Paid, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).Status);
        Assert.Equal(ExamPurchaseStatus.Active, (await db.ExamPurchases.SingleAsync(p => p.Id == purchaseId)).Status);
        var stored = await db.PaymentWebhookEvents.SingleAsync(e => e.PaymentId == payment.Id);
        Assert.Equal(($"status:{payment.InvoiceNumber}:Paid", true), (stored.GatewayEventId, stored.SignatureValid));
    }

    [Fact]
    public async Task A_pending_answer_changes_nothing_and_doku_is_asked_at_most_every_30_seconds()
    {
        await using var app = new AppFactory(database);
        var (_, payment) = await PendingAsync(app);
        var handler = new DokuGatewayTests.StubHandler(HttpStatusCode.OK, Status(payment.InvoiceNumber, "PENDING"));
        using var scope = app.Services.CreateScope();
        var check = Check(app, scope, handler);

        Assert.False(await check.HandleAsync(payment.Id, CancellationToken.None));
        Assert.False(await check.HandleAsync(payment.Id, CancellationToken.None));

        Assert.Single(handler.Requests);
        await using var db = database.CreateContext();
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).Status);
        Assert.False(await db.PaymentWebhookEvents.AnyAsync(e => e.PaymentId == payment.Id));
    }

    private static string Status(string invoice, string status) =>
        $$$"""{"order":{"invoice_number":"{{{invoice}}}","amount":49000},"transaction":{"status":"{{{status}}}","type":"PAYMENT"}}""";

    private static CheckPaymentStatus Check(AppFactory app, IServiceScope scope, DokuGatewayTests.StubHandler handler)
    {
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var doku = new DokuGateway(
            new HttpClient(handler) { BaseAddress = new Uri(DokuOptions.SandboxUrl) }, Microsoft.Extensions.Options.Options.Create(Doku), app.Clock, NullLogger<DokuGateway>.Instance);
        var apply = new ApplyGatewayNotification(db, doku, app.Clock, scope.ServiceProvider.GetRequiredService<IPaymentJobs>());
        return new CheckPaymentStatus(db, doku, apply, new MemoryCache(new MemoryCacheOptions()));
    }

    private async Task<(long PurchaseId, Payment Payment)> PendingAsync(AppFactory app)
    {
        await using var db = database.CreateContext();
        var user = await db.SaveAsync(TestData.User().Build());
        var exam = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1).Build());
        var purchase = new ExamPurchase { UserId = user.Id, ExamId = exam.Id, PricePaidIdr = 49_000, Status = ExamPurchaseStatus.AwaitingPayment };
        db.ExamPurchases.Add(purchase);
        await db.SaveChangesAsync();
        var order = $"LSI-20261024-{TestData.Unique()[..4].ToUpperInvariant()}";
        var payment = new Payment
        {
            OrderNumber = order, InvoiceNumber = order, UserId = user.Id, ExamPurchaseId = purchase.Id, AmountIdr = 49_000,
            Gateway = PaymentGateways.Doku, Method = PaymentMethod.VirtualAccount, Channel = "BCA", VaNumber = "1900800000371626",
            CreatedAt = app.Clock.UtcNow, ExpiresAt = app.Clock.UtcNow.AddDays(1),
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return (purchase.Id, payment);
    }
}
