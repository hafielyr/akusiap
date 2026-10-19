using Akusiap.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Akusiap.Infrastructure.Persistence.Configurations;

public sealed class PlanConfiguration : IEntityTypeConfiguration<Plan>
{
    public void Configure(EntityTypeBuilder<Plan> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_plans_max_own_questions", "max_own_questions >= 0");
            t.HasCheckConstraint("ck_plans_max_own_exams", "max_own_exams >= 0");
            t.HasCheckConstraint("ck_plans_max_import_rows", "max_import_rows >= 0");
        });
        builder.HasIndex(p => p.Code).IsUnique();
        builder.HasMany(p => p.Prices).WithOne(pp => pp.Plan).HasForeignKey(pp => pp.PlanId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class PlanPriceConfiguration : IEntityTypeConfiguration<PlanPrice>
{
    public void Configure(EntityTypeBuilder<PlanPrice> builder)
    {
        var allowed = string.Join(", ", PlanPrice.AllowedDurationMonths);
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_plan_prices_duration_months", $"duration_months IN ({allowed})");
            t.HasCheckConstraint("ck_plan_prices_price_idr", "price_idr >= 0");
        });
        builder.HasIndex(pp => new { pp.PlanId, pp.DurationMonths }).IsUnique();
        // Lets a subscription's (plan_price_id, plan_id) reference this pair, so price and plan always agree.
        builder.HasAlternateKey(pp => new { pp.Id, pp.PlanId });
    }
}

public sealed class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_subscriptions_auto_debit_method",
                $"renewal_mode = '{RenewalMode.Manual}' OR saved_payment_method_id IS NOT NULL");
            t.HasCheckConstraint(
                "ck_subscriptions_active_period",
                $"status <> '{SubscriptionStatus.Active}' OR " +
                "(current_period_start IS NOT NULL AND current_period_end > current_period_start)");
        });
        builder.Property(s => s.Reminder7dSentAt).HasColumnName("reminder_7d_sent_at");
        builder.Property(s => s.Reminder1dSentAt).HasColumnName("reminder_1d_sent_at");
        builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(s => s.PlanPrice)
            .WithMany()
            .HasForeignKey(s => new { s.PlanPriceId, s.PlanId })
            .HasPrincipalKey(pp => new { pp.Id, pp.PlanId })
            .OnDelete(DeleteBehavior.NoAction);
        // At most one live package of each plan per user; renewals extend the same row.
        builder.HasIndex(s => new { s.UserId, s.PlanId }, "ux_subscriptions_one_per_plan")
            .HasDatabaseName("ux_subscriptions_one_per_plan")
            .IsUnique()
            .HasFilter($"status NOT IN ('{SubscriptionStatus.Abandoned}', '{SubscriptionStatus.Refunded}')");
        builder.HasOne<Akusiap.Domain.Payments.SavedPaymentMethod>()
            .WithMany()
            .HasForeignKey(s => s.SavedPaymentMethodId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(s => s.SavedPaymentMethodId);
        // Reminder and expiry jobs.
        builder.HasIndex(s => s.CurrentPeriodEnd, "ix_subscriptions_period_end")
            .HasDatabaseName("ix_subscriptions_period_end").HasFilter($"status = '{SubscriptionStatus.Active}'");
    }
}

public sealed class ExamPurchaseConfiguration : IEntityTypeConfiguration<ExamPurchase>
{
    public void Configure(EntityTypeBuilder<ExamPurchase> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_exam_purchases_price_paid_idr", "price_paid_idr >= 0");
            t.HasCheckConstraint(
                "ck_exam_purchases_active_paid",
                $"status <> '{ExamPurchaseStatus.Active}' OR (purchased_at IS NOT NULL AND access_expires_at IS NOT NULL)");
        });
        builder.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(p => p.Exam).WithMany().HasForeignKey(p => p.ExamId).OnDelete(DeleteBehavior.NoAction);
        // Buying the same exam again extends the same row.
        builder.HasIndex(p => new { p.UserId, p.ExamId }, "ux_exam_purchases_one_per_exam")
            .HasDatabaseName("ux_exam_purchases_one_per_exam")
            .IsUnique()
            .HasFilter($"status NOT IN ('{ExamPurchaseStatus.Abandoned}', '{ExamPurchaseStatus.Refunded}')");
    }
}
