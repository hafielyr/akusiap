using Akusiap.Application.Access;
using Akusiap.Application.Payments;
using Akusiap.Domain.Access;
using Akusiap.Domain.Notifications;
using Akusiap.Domain.Payments;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Akusiap.Web.Payments;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>PAY-08: package reminders go out once each (test clock); ended access and stale Pending payments expire.</summary>
[Collection(UsesDatabase.Name)]
public sealed class ExpiryJobsTests(PostgresFixture database)
{
    [Fact]
    public async Task Reminders_are_sent_7_days_and_1_day_before_the_end_once_each()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Site:BaseUrl", "https://akusiap.test/"));
        var user = TestData.User().Build();
        long subscriptionId, planPriceId;
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(user);
            var plan = await db.Plans.SingleAsync(p => p.Code == PlanCode.Learner);
            var price = await db.PlanPrices.FirstOrDefaultAsync(p => p.PlanId == plan.Id)
                ?? db.PlanPrices.Add(new PlanPrice { PlanId = plan.Id, DurationMonths = 1, PriceIdr = 99_000 }).Entity;
            // Ends at 14:00 WIB, 7 Jakarta days after the test clock's 09:00 WIB start.
            var subscription = new Subscription
            {
                UserId = user.Id, PlanId = plan.Id, PlanPrice = price, Status = SubscriptionStatus.Active,
                CurrentPeriodStart = app.Clock.UtcNow.AddDays(-23), CurrentPeriodEnd = app.Clock.UtcNow.AddDays(7).AddHours(5), CreatedAt = app.Clock.UtcNow.AddDays(-23),
            };
            db.Subscriptions.Add(subscription);
            await db.SaveChangesAsync();
            (subscriptionId, planPriceId) = (subscription.Id, price.Id);
        }

        await RunRemindersAsync(site);
        await RunRemindersAsync(site);
        var afterSeven = app.Email.To(user.Email!);
        app.Clock.Advance(TimeSpan.FromDays(6));
        await RunRemindersAsync(site);
        await RunRemindersAsync(site);
        app.Clock.Advance(TimeSpan.FromDays(2));
        await RunRemindersAsync(site);

        var sent = app.Email.To(user.Email!);
        Assert.Single(afterSeven);
        Assert.Equal(2, sent.Count);
        Assert.Contains("dalam 7 hari", sent[0].Subject, StringComparison.Ordinal);
        Assert.Contains("besok", sent[1].Subject, StringComparison.Ordinal);
        Assert.All(sent, m => Assert.Contains($"https://akusiap.test/checkout?planPrice={planPriceId}", m.TextBody!, StringComparison.Ordinal));
        await using var check = database.CreateContext();
        var row = await check.Subscriptions.SingleAsync(s => s.Id == subscriptionId);
        Assert.NotNull(row.Reminder7dSentAt);
        Assert.NotNull(row.Reminder1dSentAt);
        Assert.Equal(
            [NotificationTemplate.Renewal7d, NotificationTemplate.Renewal1d],
            await check.Notifications.Where(n => n.UserId == user.Id && n.Status == NotificationStatus.Sent).OrderBy(n => n.Id).Select(n => n.TemplateCode).ToListAsync());
    }

    [Fact]
    public async Task Ended_access_expires_and_stale_pending_payments_expire_and_abandon_their_item()
    {
        await using var app = new AppFactory(database);
        var user = TestData.User().Build();
        var now = app.Clock.UtcNow;
        ExamPurchase ended, running, awaiting;
        Payment stale, fresh;
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(user);
            var exams = new[] { TestData.Exam().Paid(49_000).Build(), TestData.Exam().Paid(49_000).Build(), TestData.Exam().Paid(49_000).Build() };
            foreach (var exam in exams)
            {
                await db.SaveAsync(exam);
            }

            ended = Purchase(user.Id, exams[0].Id, ExamPurchaseStatus.Active, now.AddMinutes(-1));
            running = Purchase(user.Id, exams[1].Id, ExamPurchaseStatus.Active, now.AddDays(3));
            awaiting = Purchase(user.Id, exams[2].Id, ExamPurchaseStatus.AwaitingPayment, null);
            db.ExamPurchases.AddRange(ended, running, awaiting);
            await db.SaveChangesAsync();
            stale = PendingPayment(user.Id, awaiting.Id, now.AddMinutes(-5));
            fresh = PendingPayment(user.Id, running.Id, now.AddMinutes(30));
            db.Payments.AddRange(stale, fresh);
            await db.SaveChangesAsync();
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ExpireAccess>().RunAsync(CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<ExpirePendingPayments>().RunAsync(CancellationToken.None);
        }

        await using var check = database.CreateContext();
        var purchases = await check.ExamPurchases.Where(p => p.UserId == user.Id).ToDictionaryAsync(p => p.Id, p => p.Status);
        var payments = await check.Payments.Where(p => p.UserId == user.Id).ToDictionaryAsync(p => p.Id, p => p.Status);
        Assert.Equal(ExamPurchaseStatus.Expired, purchases[ended.Id]);
        Assert.Equal(ExamPurchaseStatus.Active, purchases[running.Id]);
        Assert.Equal(ExamPurchaseStatus.Abandoned, purchases[awaiting.Id]);
        Assert.Equal(PaymentStatus.Expired, payments[stale.Id]);
        Assert.Equal(PaymentStatus.Pending, payments[fresh.Id]);
    }

    private static async Task RunRemindersAsync(WebApplicationFactory<Program> site)
    {
        await using var scope = site.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RenewalReminderJob>().RunAsync(CancellationToken.None);
    }

    private static ExamPurchase Purchase(Guid userId, long examId, ExamPurchaseStatus status, DateTime? ends) => new()
    {
        UserId = userId,
        ExamId = examId,
        Status = status,
        PricePaidIdr = 49_000,
        PurchasedAt = ends is null ? null : ends.Value.AddDays(-30),
        AccessExpiresAt = ends,
    };

    private static Payment PendingPayment(Guid userId, long purchaseId, DateTime expiresAt)
    {
        var order = $"LSI-{TestData.Unique()}";
        return new Payment
        {
            OrderNumber = order, InvoiceNumber = order, UserId = userId, ExamPurchaseId = purchaseId, AmountIdr = 49_000,
            Method = PaymentMethod.QRIS, QrString = "qr", Status = PaymentStatus.Pending, CreatedAt = expiresAt.AddMinutes(-60), ExpiresAt = expiresAt,
        };
    }
}
