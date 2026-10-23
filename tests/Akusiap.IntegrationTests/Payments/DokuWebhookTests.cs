using System.Net;
using System.Text;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Payments;
using Akusiap.Infrastructure.Payments;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>
/// PAY-04 with PAY-06: a notification signed the way DOKU signs it, posted to <c>/webhooks/doku</c>, pays and unlocks
/// the purchase; one signed with another key is stored but never applied.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed class DokuWebhookTests(PostgresFixture database)
{
    private static readonly DokuOptions Doku = new() { ClientId = "BRN-0000-1234567890123", SecretKey = "SK-test-secret" };

    [Fact]
    public async Task A_dokus_signed_success_notification_unlocks_the_exam()
    {
        await using var app = new AppFactory(database);
        await using var doku = WithDoku(app);
        var (purchaseId, payment) = await PendingAsync(app);

        using var response = await PostAsync(doku, DokuGateway.CreateSampleNotification(Doku, payment.InvoiceNumber, "SUCCESS", payment.AmountIdr, app.Clock.UtcNow));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(PaymentStatus.Paid, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).Status);
        Assert.Equal(ExamPurchaseStatus.Active, (await db.ExamPurchases.SingleAsync(p => p.Id == purchaseId)).Status);
    }

    [Fact]
    public async Task A_notification_signed_with_another_key_is_never_applied()
    {
        await using var app = new AppFactory(database);
        await using var doku = WithDoku(app);
        var (_, payment) = await PendingAsync(app);
        var forged = DokuGateway.CreateSampleNotification(new DokuOptions { ClientId = Doku.ClientId, SecretKey = "SK-attacker" }, payment.InvoiceNumber, "SUCCESS", payment.AmountIdr, app.Clock.UtcNow);

        using var response = await PostAsync(doku, forged);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);                                  // stored, acknowledged, not applied
        await using var db = database.CreateContext();
        Assert.Equal(PaymentStatus.Pending, (await db.Payments.SingleAsync(p => p.Id == payment.Id)).Status);
        Assert.False((await db.PaymentWebhookEvents.SingleAsync(e => e.PaymentId == payment.Id)).SignatureValid);
    }

    private static WebApplicationFactory<Program> WithDoku(AppFactory app) => app.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
    {
        services.RemoveAll<IPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(new DokuGateway(
            new HttpClient { BaseAddress = new Uri(DokuOptions.SandboxUrl) }, Microsoft.Extensions.Options.Options.Create(Doku), app.Clock, NullLogger<DokuGateway>.Instance));
    }));

    private async Task<(long PurchaseId, Payment Payment)> PendingAsync(AppFactory app)
    {
        await using var db = database.CreateContext();
        var user = await db.SaveAsync(TestData.User().Build());
        var exam = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1).Build());
        var purchase = new ExamPurchase { UserId = user.Id, ExamId = exam.Id, PricePaidIdr = 49_000, Status = ExamPurchaseStatus.AwaitingPayment };
        db.ExamPurchases.Add(purchase);
        await db.SaveChangesAsync();
        var order = $"LSI-20261001-{TestData.Unique()[..4].ToUpperInvariant()}";
        var payment = new Payment
        {
            OrderNumber = order, InvoiceNumber = order, UserId = user.Id, ExamPurchaseId = purchase.Id, AmountIdr = 49_000,
            Gateway = PaymentGateways.Doku, Method = PaymentMethod.VirtualAccount, Channel = "BCA", VaNumber = "1900800000371404",
            CreatedAt = app.Clock.UtcNow, ExpiresAt = app.Clock.UtcNow.AddDays(1),
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return (purchase.Id, payment);
    }

    private static async Task<HttpResponseMessage> PostAsync(WebApplicationFactory<Program> app, GatewayNotification notification)
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
}
