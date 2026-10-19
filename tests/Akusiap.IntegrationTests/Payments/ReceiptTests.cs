using System.Net;
using System.Text;
using Akusiap.Domain.Notifications;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Payments;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>PAY-07: a paid payment sends one Indonesian receipt and records the notification.</summary>
[Collection(UsesDatabase.Name)]
public sealed class ReceiptTests(PostgresFixture database)
{
    [Fact]
    public async Task Paying_with_the_fake_gateway_sends_one_receipt_and_records_it()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().English().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Titled("Tryout Kwitansi").Paid(49_000, 30).WithQuestions(1, trial: 1).Build())).Id;
        }

        using var placed = await learner.PostFormAsync($"/checkout?exam={examId}", new Dictionary<string, string> { ["Channel"] = "VA-BCA", ["AcceptTerms"] = "true" });
        var order = placed.Headers.Location!.OriginalString["/bayar/".Length..];
        var paid = FakePaymentGateway.CreateNotification(order, "PAID", 49_000);
        using var client = app.CreateClient();
        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/webhooks/doku", UriKind.Relative)) { Content = new StringContent(paid.Body, Encoding.UTF8, "application/json") };
            request.Headers.TryAddWithoutValidation(FakePaymentGateway.SignatureHeader, paid.Headers[FakePaymentGateway.SignatureHeader]);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var receipt = Assert.Single(app.Email.To(user.Email!), m => m.Subject.Contains(order, StringComparison.Ordinal));
        Assert.Equal($"Bukti pembayaran Akusiap {order}", receipt.Subject); // Indonesian even for English users
        Assert.Contains("Tryout Kwitansi", receipt.TextBody, StringComparison.Ordinal);
        Assert.Contains("Rp49.000", receipt.TextBody, StringComparison.Ordinal);
        Assert.Contains("Virtual account BCA", receipt.TextBody, StringComparison.Ordinal);
        Assert.Contains("Akses sampai: 31 Oktober 2026", receipt.TextBody, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        var notification = await check.Notifications.SingleAsync(n => n.UserId == user.Id);
        Assert.Equal((NotificationChannel.Email, NotificationTemplate.Receipt, NotificationStatus.Sent), (notification.Channel, notification.TemplateCode, notification.Status));
        Assert.Contains(order, notification.Payload, StringComparison.Ordinal);
    }
}
