using Akusiap.Domain.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Akusiap.Infrastructure.Persistence.Configurations;

// Payment tables from schema.sql (PAY-01). Enum CHECKs come from AppDbContext; the gateway columns allow only DOKU.

public sealed class SavedPaymentMethodConfiguration : IEntityTypeConfiguration<SavedPaymentMethod>
{
    public void Configure(EntityTypeBuilder<SavedPaymentMethod> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("ck_saved_payment_methods_gateway", $"gateway = '{PaymentGateways.Doku}'"));
        builder.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(m => m.UserId, "ix_saved_payment_methods_user").HasDatabaseName("ix_saved_payment_methods_user");
    }
}

public sealed class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_promo_codes_discount_percent", "discount_percent > 0 AND discount_percent <= 100");
            t.HasCheckConstraint("ck_promo_codes_discount_amount_idr", "discount_amount_idr > 0");
            t.HasCheckConstraint("ck_promo_codes_max_uses", "max_uses > 0");
            t.HasCheckConstraint("ck_promo_codes_used_count", "used_count >= 0");
            t.HasCheckConstraint("ck_promo_codes_one_discount", "num_nonnulls(discount_percent, discount_amount_idr) = 1");
        });
        builder.Property(p => p.DiscountPercent).HasPrecision(5, 2);
        // ux_promo_codes_code ON (upper(code)) is created in the migration (expression index).
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_payments_amount_idr", "amount_idr >= 0");
            t.HasCheckConstraint("ck_payments_discount_idr", "discount_idr >= 0");
            t.HasCheckConstraint("ck_payments_tax_idr", "tax_idr >= 0");
            t.HasCheckConstraint("ck_payments_gateway_fee_idr", "gateway_fee_idr >= 0");
            t.HasCheckConstraint("ck_payments_retry_count", "retry_count >= 0");
            t.HasCheckConstraint("ck_payments_gateway", $"gateway = '{PaymentGateways.Doku}'");
            t.HasCheckConstraint("ck_payments_one_item", "num_nonnulls(subscription_id, exam_purchase_id) = 1");
            t.HasCheckConstraint(
                "ck_payments_paid_at",
                $"status NOT IN ('{PaymentStatus.Paid}', '{PaymentStatus.Refunded}') OR paid_at IS NOT NULL");
            t.HasCheckConstraint(
                "ck_payments_va_number",
                $"method <> '{PaymentMethod.VirtualAccount}' OR status <> '{PaymentStatus.Pending}' OR va_number IS NOT NULL");
            t.HasCheckConstraint(
                "ck_payments_qr_string",
                $"method <> '{PaymentMethod.QRIS}' OR status <> '{PaymentStatus.Pending}' OR qr_string IS NOT NULL");
        });
        builder.HasIndex(p => p.OrderNumber).IsUnique();
        builder.HasIndex(p => p.InvoiceNumber).IsUnique();
        builder.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(p => p.Subscription).WithMany().HasForeignKey(p => p.SubscriptionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(p => p.ExamPurchase).WithMany().HasForeignKey(p => p.ExamPurchaseId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(p => p.PromoCode).WithMany().HasForeignKey(p => p.PromoCodeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(p => p.SavedPaymentMethod).WithMany().HasForeignKey(p => p.SavedPaymentMethodId).OnDelete(DeleteBehavior.NoAction);

        // BR-21: one pending payment per item.
        builder.HasIndex(p => p.SubscriptionId, "ux_payments_one_pending_subscription")
            .HasDatabaseName("ux_payments_one_pending_subscription")
            .IsUnique()
            .HasFilter($"status = '{PaymentStatus.Pending}' AND subscription_id IS NOT NULL");
        builder.HasIndex(p => p.ExamPurchaseId, "ux_payments_one_pending_exam_purchase")
            .HasDatabaseName("ux_payments_one_pending_exam_purchase")
            .IsUnique()
            .HasFilter($"status = '{PaymentStatus.Pending}' AND exam_purchase_id IS NOT NULL");
        builder.HasIndex(p => new { p.UserId, p.CreatedAt }, "ix_payments_user")
            .HasDatabaseName("ix_payments_user")
            .IsDescending(false, true);
        builder.HasIndex(p => p.PromoCodeId, "ix_payments_promo").HasDatabaseName("ix_payments_promo");
        builder.HasIndex(p => p.SavedPaymentMethodId, "ix_payments_saved_method").HasDatabaseName("ix_payments_saved_method");
        // Status-check job (ADM-35).
        builder.HasIndex(p => p.ExpiresAt, "ix_payments_pending_deadline")
            .HasDatabaseName("ix_payments_pending_deadline")
            .HasFilter($"status = '{PaymentStatus.Pending}'");
    }
}

public sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_refunds_amount_idr", "amount_idr > 0");
            t.HasCheckConstraint("ck_refunds_attempt_count", "attempt_count >= 0");
            t.HasCheckConstraint(
                "ck_refunds_route_required",
                $"status IN ('{RefundStatus.Requested}', '{RefundStatus.Rejected}') OR refund_route IS NOT NULL");
            t.HasCheckConstraint(
                "ck_refunds_link_required",
                $"status <> '{RefundStatus.AwaitingDetails}' OR refund_link_url IS NOT NULL");
        });
        builder.HasOne(r => r.Payment).WithMany().HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(r => r.DecidedByUser).WithMany().HasForeignKey(r => r.DecidedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(r => r.PaymentId, "ix_refunds_payment").HasDatabaseName("ix_refunds_payment");
        builder.HasIndex(r => r.DecidedByUserId, "ix_refunds_decided_by").HasDatabaseName("ix_refunds_decided_by");
        // Open refunds (ADM-36).
        builder.HasIndex(r => r.Status, "ix_refunds_open")
            .HasDatabaseName("ix_refunds_open")
            .HasFilter($"status IN ('{RefundStatus.Requested}', '{RefundStatus.AwaitingDetails}', '{RefundStatus.Failed}')");
    }
}

public sealed class PaymentWebhookEventConfiguration : IEntityTypeConfiguration<PaymentWebhookEvent>
{
    public void Configure(EntityTypeBuilder<PaymentWebhookEvent> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("ck_payment_webhook_events_gateway", $"gateway = '{PaymentGateways.Doku}'"));
        builder.Property(e => e.Payload).HasColumnType("jsonb");
        // BR-26: each gateway event is processed once.
        builder.HasIndex(e => new { e.Gateway, e.GatewayEventId }).IsUnique();
        builder.HasOne(e => e.Payment).WithMany().HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(e => e.PaymentId, "ix_payment_webhook_events_payment").HasDatabaseName("ix_payment_webhook_events_payment");
    }
}
