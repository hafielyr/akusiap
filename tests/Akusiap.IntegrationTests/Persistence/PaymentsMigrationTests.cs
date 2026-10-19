using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Payments;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Database;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Persistence;

/// <summary>PAY-01: the payment tables of schema.sql and the rules the database enforces.</summary>
public sealed class PaymentsMigrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 19, 2, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string[]> SchemaColumns = new()
    {
        ["saved_payment_methods"] =
        [
            "id int8", "user_id uuid", "gateway text", "type text", "display_label text", "gateway_token text", "is_active bool",
            "created_at timestamptz",
        ],
        ["promo_codes"] =
        [
            "id int8", "code text", "discount_percent numeric?", "discount_amount_idr int8?", "max_uses int4?", "used_count int4",
            "expires_at timestamptz?",
        ],
        ["payments"] =
        [
            "id int8", "order_number text", "user_id uuid", "subscription_id int8?", "exam_purchase_id int8?", "promo_code_id int8?",
            "saved_payment_method_id int8?", "amount_idr int8", "discount_idr int8", "tax_idr int8", "gateway_fee_idr int8?",
            "gateway text", "method text", "channel text?", "va_number text?", "qr_string text?", "deeplink_url text?", "status text",
            "expires_at timestamptz?", "invoice_number text", "gateway_request_id text?", "is_late_payment bool",
            "failure_reason text?", "retry_count int4", "created_at timestamptz", "paid_at timestamptz?",
        ],
        ["refunds"] =
        [
            "id int8", "payment_id int8", "amount_idr int8", "reason text", "status text", "is_auto_approved bool",
            "refund_route text?", "refund_link_url text?", "refund_link_expires_at timestamptz?", "attempt_count int4",
            "requested_at timestamptz", "decided_by_user_id uuid?", "decided_at timestamptz?", "gateway_refund_id text?",
        ],
        ["payment_webhook_events"] =
        [
            "id int8", "gateway text", "gateway_event_id text", "payment_id int8?", "payload jsonb", "signature_valid bool",
            "received_at timestamptz", "processed_at timestamptz?", "processing_error text?",
        ],
    };

    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Every_schema_column_exists_with_the_right_type_and_nullability()
    {
        await using var db = _database.CreateContext();

        Assert.Empty(await SchemaAssertions.FindColumnProblemsAsync(db, SchemaColumns));
    }

    [Fact]
    public async Task Unique_keys_exist()
    {
        await using var db = _database.CreateContext();

        Assert.True(await SchemaAssertions.HasUniqueIndexAsync(db, "payments", "order_number"));
        Assert.True(await SchemaAssertions.HasUniqueIndexAsync(db, "payments", "invoice_number"));
        Assert.True(await SchemaAssertions.HasUniqueIndexAsync(db, "payment_webhook_events", "gateway, gateway_event_id"));
        Assert.True(await SchemaAssertions.HasUniqueIndexAsync(db, "promo_codes", "upper(code)"));
    }

    [Fact]
    public async Task One_of_each_entity_round_trips()
    {
        var seed = await SeedAsync();
        await using var db = _database.CreateContext();
        db.PromoCodes.Add(new PromoCode { Code = "HEMAT10", DiscountPercent = 10.5m });
        db.Refunds.Add(new Refund { PaymentId = seed.PaymentId, AmountIdr = 49_000, Reason = "Dobel", RequestedAt = Now });
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent { GatewayEventId = "req-1", PaymentId = seed.PaymentId, Payload = """{"a": 1}""", SignatureValid = true, ReceivedAt = Now });
        await db.SaveChangesAsync();

        await using var check = _database.CreateContext();
        var payment = await check.Payments.SingleAsync(p => p.Id == seed.PaymentId);
        Assert.Equal((PaymentMethod.QRIS, PaymentStatus.Pending, PaymentGateways.Doku), (payment.Method, payment.Status, payment.Gateway));
        Assert.Equal(10.5m, (await check.PromoCodes.SingleAsync()).DiscountPercent);
        Assert.Equal(RefundStatus.Requested, (await check.Refunds.SingleAsync()).Status);
        Assert.Contains("\"a\": 1", (await check.PaymentWebhookEvents.SingleAsync()).Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Database_rejects_rows_that_break_the_rules()
    {
        var seed = await SeedAsync();
        await using var db = _database.CreateContext();
        db.PromoCodes.Add(new PromoCode { Code = "HEMAT", DiscountAmountIdr = 5_000 });
        db.Refunds.Add(new Refund { PaymentId = seed.PaymentId, AmountIdr = 1_000, Reason = "Uji", RequestedAt = Now });
        db.PaymentWebhookEvents.Add(new PaymentWebhookEvent { GatewayEventId = "req-9", Payload = "{}", ReceivedAt = Now });
        await db.SaveChangesAsync();
        var insertPayment =
            "INSERT INTO payments (order_number, invoice_number, user_id, exam_purchase_id, amount_idr, discount_idr, tax_idr, gateway, method, " +
            "status, is_late_payment, retry_count, created_at, qr_string) " +
            $"SELECT 'AKS-20261019-ZZZZ', 'INV-2', user_id, exam_purchase_id, 1, 0, 0, 'DOKU', 'QRIS', 'Pending', false, 0, now(), 'qr' FROM payments WHERE id = {seed.PaymentId}";
        (string Sql, string Constraint)[] cases =
        [
            ("UPDATE payments SET subscription_id = NULL, exam_purchase_id = NULL", "ck_payments_one_item"),
            ("UPDATE payments SET status = 'Paid'", "ck_payments_paid_at"),
            ("UPDATE payments SET qr_string = NULL", "ck_payments_qr_string"),
            ("UPDATE payments SET method = 'VirtualAccount'", "ck_payments_va_number"),
            ("UPDATE payments SET gateway = 'Xendit'", "ck_payments_gateway"),
            ("UPDATE payments SET method = 'Cash'", "ck_payments_method"),
            ("UPDATE payments SET amount_idr = -1", "ck_payments_amount_idr"),
            (insertPayment, "ux_payments_one_pending_exam_purchase"),
            ("INSERT INTO promo_codes (code, discount_percent, used_count) VALUES ('hemat', 5, 0)", "ux_promo_codes_code"),
            ("UPDATE promo_codes SET discount_percent = 10", "ck_promo_codes_one_discount"),
            ("UPDATE refunds SET status = 'Approved'", "ck_refunds_route_required"),
            ("UPDATE refunds SET status = 'AwaitingDetails', refund_route = 'RefundLink'", "ck_refunds_link_required"),
            ("INSERT INTO payment_webhook_events (gateway, gateway_event_id, payload, signature_valid, received_at) VALUES ('DOKU', 'req-9', '{{}}', false, now())", "ix_payment_webhook_events_gateway_gateway_event_id"),
            ($"UPDATE subscriptions SET saved_payment_method_id = 999999 WHERE id = {seed.SubscriptionId}", "fk_subscriptions_saved_payment_methods_saved_payment_method_id"),
        ];

        Assert.Empty(await SchemaAssertions.FindUnenforcedRulesAsync(db, cases));
    }

    private sealed record Seed(long PaymentId, long SubscriptionId);

    private async Task<Seed> SeedAsync()
    {
        await using var db = _database.CreateContext();
        var user = new User { Id = Guid.NewGuid(), UserName = "rina@example.com", Email = "rina@example.com", DisplayName = "Rina", CreatedAt = Now };
        var exam = new Exam
        {
            RulePreset = new RulePreset { Name = "Standar", IsDefault = true },
            Title = "Tryout",
            PriceIdr = 49_000,
            AccessDays = 30,
            Status = ExamStatus.Published,
            CreatedAt = Now,
        };
        var plan = new Plan { Code = PlanCode.Learner, Name = "Paket Pelajar", IncludesAllExams = true };
        var price = new PlanPrice { Plan = plan, DurationMonths = 1, PriceIdr = 99_000 };
        db.AddRange(user, exam, plan, price);
        await db.SaveChangesAsync();
        var purchase = new ExamPurchase { UserId = user.Id, ExamId = exam.Id, PricePaidIdr = 49_000 };
        var subscription = new Subscription { UserId = user.Id, PlanId = plan.Id, PlanPriceId = price.Id, CreatedAt = Now };
        db.AddRange(purchase, subscription);
        await db.SaveChangesAsync();
        var payment = new Payment
        {
            OrderNumber = "AKS-20261019-AAAA",
            InvoiceNumber = "AKS-20261019-AAAA",
            UserId = user.Id,
            ExamPurchaseId = purchase.Id,
            AmountIdr = 49_000,
            Method = PaymentMethod.QRIS,
            QrString = "qr",
            CreatedAt = Now,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync();
        return new Seed(payment.Id, subscription.Id);
    }
}
