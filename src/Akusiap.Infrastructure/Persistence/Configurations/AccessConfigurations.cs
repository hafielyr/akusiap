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
        builder.Property(p => p.Name).HasMaxLength(100);
        builder.HasIndex(p => p.Code).IsUnique();
        builder.HasMany(p => p.Prices).WithOne(pp => pp.Plan).HasForeignKey(pp => pp.PlanId).OnDelete(DeleteBehavior.Restrict);
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
        // Lets a subscription's (PlanPriceId, PlanId) reference this pair, so price and plan always agree.
        builder.HasAlternateKey(pp => new { pp.PlanPriceId, pp.PlanId });
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
                $"status <> '{SubscriptionStatus.Active}' OR current_period_end > current_period_start");
        });
        builder.Property(s => s.GatewaySubscriptionId).HasMaxLength(200);
        builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Plan).WithMany().HasForeignKey(s => s.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.PlanPrice)
            .WithMany()
            .HasForeignKey(s => new { s.PlanPriceId, s.PlanId })
            .HasPrincipalKey(pp => new { pp.PlanPriceId, pp.PlanId })
            .OnDelete(DeleteBehavior.Restrict);
        // SavedPaymentMethodId has no foreign key until PAY-01 adds the table (D-22).
        builder.HasIndex(s => s.SavedPaymentMethodId);
        // Reminder and expiry jobs scan active subscriptions by end date.
        builder.HasIndex(s => s.CurrentPeriodEnd).HasFilter($"status = '{SubscriptionStatus.Active}'");
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
                "ck_exam_purchases_active_paid", $"status <> '{ExamPurchaseStatus.Active}' OR purchased_at IS NOT NULL");
        });
        builder.HasOne(p => p.User).WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(p => p.Exam).WithMany().HasForeignKey(p => p.ExamId).OnDelete(DeleteBehavior.Restrict);
    }
}
