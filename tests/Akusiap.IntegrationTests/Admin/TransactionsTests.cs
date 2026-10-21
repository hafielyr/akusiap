using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Payments;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>PAY-10: admin transactions. Every query includes a unique user name, so other tests' payments never match.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class TransactionsTests(PostgresFixture database)
{
    [Fact]
    public async Task Filters_combine_and_the_export_matches_them()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var token = TestData.Unique();
        var day1 = TestClock.DefaultStart; // 1 Oct 2026, 09:00 WIB
        var paidQris = await PaymentAsync(token, PaymentStatus.Paid, PaymentMethod.QRIS, day1);
        var paidVa = await PaymentAsync(token, PaymentStatus.Paid, PaymentMethod.VirtualAccount, day1.AddDays(1));
        var pendingQris = await PaymentAsync(token, PaymentStatus.Pending, PaymentMethod.QRIS, day1.AddDays(1));
        var oldPaidQris = await PaymentAsync(token, PaymentStatus.Paid, PaymentMethod.QRIS, day1.AddDays(-10));

        Assert.Equal(new[] { pendingQris, paidVa, paidQris, oldPaidQris }, await OrdersAsync(admin, $"q={token}"));
        Assert.Equal(new[] { paidQris, oldPaidQris }, await OrdersAsync(admin, $"q={token}&Status=Paid&metode=QRIS"));
        Assert.Equal(new[] { paidQris }, await OrdersAsync(admin, $"q={token}&Status=Paid&metode=QRIS&dari=2026-09-30&sampai=2026-10-01"));
        Assert.Equal(new[] { pendingQris, paidVa }, await OrdersAsync(admin, $"q={token}&dari=2026-10-02"));

        using var csv = await admin.GetAsync(new Uri($"/admin/transaksi?handler=Csv&q={token}&Status=Paid&metode=QRIS", UriKind.Relative));
        var lines = (await csv.Content.ReadAsStringAsync()).TrimStart('﻿').Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r')).ToList();
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("order_number,created_at_wib", lines[0], StringComparison.Ordinal);
        Assert.Equal(new[] { paidQris, oldPaidQris }, lines.Skip(1).Select(l => l.Split(',')[0]));
    }

    [Fact]
    public async Task Failed_view_shows_reason_and_retries_and_the_detail_shows_events()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var token = TestData.Unique();
        var failed = await PaymentAsync(token, PaymentStatus.Failed, PaymentMethod.EWallet, TestClock.DefaultStart, reason: "Saldo tidak cukup", retries: 2);
        long id;
        await using (var db = database.CreateContext())
        {
            var payment = await db.Payments.SingleAsync(p => p.OrderNumber == failed);
            id = payment.Id;
            db.PaymentWebhookEvents.Add(new PaymentWebhookEvent { GatewayEventId = "evt-" + token, PaymentId = id, Payload = """{"status":"FAILED"}""", SignatureValid = true, ReceivedAt = TestClock.DefaultStart });
            await db.SaveChangesAsync();
        }

        var list = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/transaksi?q={token}&Status=Failed", UriKind.Relative)));
        var detail = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/transaksi/{id}", UriKind.Relative)));
        using var missing = await admin.GetAsync(new Uri("/admin/transaksi/987654321", UriKind.Relative));

        Assert.Contains("Saldo tidak cukup · 2 kali dicoba ulang", list, StringComparison.Ordinal);
        Assert.Contains("evt-" + token, detail, StringComparison.Ordinal);
        Assert.Contains("\"status\": \"FAILED\"", detail, StringComparison.Ordinal); // jsonb normalizes spacing
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Payments_can_never_be_deleted()
    {
        var token = TestData.Unique();
        var order = await PaymentAsync(token, PaymentStatus.Paid, PaymentMethod.QRIS, TestClock.DefaultStart);
        await using var db = database.CreateContext();
        db.Payments.Remove(await db.Payments.SingleAsync(p => p.OrderNumber == order));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Learners_cannot_see_transactions()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var response = await learner.GetAsync(new Uri("/admin/transaksi", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<string> PaymentAsync(string token, PaymentStatus status, PaymentMethod method, DateTime createdAt, string? reason = null, int retries = 0)
    {
        await using var db = database.CreateContext();
        var user = await db.SaveAsync(TestData.User().Named($"Pembeli {token}").Build());
        var exam = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1).Build());
        var purchase = new ExamPurchase { UserId = user.Id, ExamId = exam.Id, PricePaidIdr = 49_000 };
        db.ExamPurchases.Add(purchase);
        await db.SaveChangesAsync();
        var order = $"LSI-{TestData.Unique()}";
        db.Payments.Add(new Payment
        {
            OrderNumber = order, InvoiceNumber = order, UserId = user.Id, ExamPurchaseId = purchase.Id, AmountIdr = 49_000,
            Method = method, Channel = method == PaymentMethod.VirtualAccount ? "BCA" : null, Status = status,
            QrString = method == PaymentMethod.QRIS ? "qr" : null, VaNumber = method == PaymentMethod.VirtualAccount ? "8808000" : null,
            CreatedAt = createdAt, PaidAt = status == PaymentStatus.Paid ? createdAt : null, FailureReason = reason, RetryCount = retries,
        });
        await db.SaveChangesAsync();
        return order;
    }

    private static async Task<string[]> OrdersAsync(HttpClient admin, string query)
    {
        var html = await admin.GetStringAsync(new Uri($"/admin/transaksi?{query}", UriKind.Relative));
        return [.. RowPattern().Matches(html).Select(m => m.Groups[1].Value)];
    }

    [GeneratedRegex("data-testid=\"tx-row\" data-order=\"([^\"]+)\"")]
    private static partial Regex RowPattern();
}
