using Akusiap.Domain.Memorization;
using Akusiap.Domain.Moderation;
using Akusiap.Domain.Notifications;
using Akusiap.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Akusiap.Infrastructure.Persistence.Configurations;

public sealed class ReviewCardConfiguration : IEntityTypeConfiguration<ReviewCard>
{
    public void Configure(EntityTypeBuilder<ReviewCard> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_review_cards_interval_days", "interval_days >= 0");
            t.HasCheckConstraint("ck_review_cards_ease_factor", "ease_factor > 0");
            t.HasCheckConstraint("ck_review_cards_repetitions", "repetitions >= 0");
            t.HasCheckConstraint("ck_review_cards_lapses", "lapses >= 0");
        });
        builder.Property(c => c.IntervalDays).HasPrecision(8, 2);
        builder.Property(c => c.EaseFactor).HasPrecision(4, 2);
        builder.HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(c => c.Question).WithMany().HasForeignKey(c => c.QuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Logs).WithOne(l => l.ReviewCard).HasForeignKey(l => l.ReviewCardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.UserId, c.QuestionId }).IsUnique();
        // Daily review queue (M-08).
        builder.HasIndex(c => new { c.UserId, c.DueAt }, "ix_review_cards_due").HasDatabaseName("ix_review_cards_due");
    }
}

public sealed class ReviewLogConfiguration : IEntityTypeConfiguration<ReviewLog>
{
    public void Configure(EntityTypeBuilder<ReviewLog> builder)
    {
        builder.Property(l => l.IntervalBeforeDays).HasPrecision(8, 2);
        builder.Property(l => l.IntervalAfterDays).HasPrecision(8, 2);
        builder.HasIndex(l => new { l.ReviewCardId, l.ReviewedAt }, "ix_review_logs_card")
            .HasDatabaseName("ix_review_logs_card")
            .IsDescending(false, true);
    }
}

public sealed class ContentReportConfiguration : IEntityTypeConfiguration<ContentReport>
{
    public void Configure(EntityTypeBuilder<ContentReport> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("ck_content_reports_target", "num_nonnulls(question_id, exam_id) >= 1"));
        builder.HasOne(r => r.ReporterUser).WithMany().HasForeignKey(r => r.ReporterUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(r => r.ResolvedByUser).WithMany().HasForeignKey(r => r.ResolvedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(r => r.Question).WithMany().HasForeignKey(r => r.QuestionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(r => r.Exam).WithMany().HasForeignKey(r => r.ExamId).OnDelete(DeleteBehavior.NoAction);
        // Admin report queue (ADM-04).
        builder.HasIndex(r => r.CreatedAt, "ix_content_reports_open")
            .HasDatabaseName("ix_content_reports_open")
            .HasFilter($"status = '{ContentReportStatus.Open}'");
    }
}

public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_notifications_language", $"language IN ('{Languages.Indonesian}', '{Languages.English}')"));
        builder.Property(n => n.Payload).HasColumnType("jsonb");
        builder.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(n => new { n.UserId, n.ScheduledAt }, "ix_notifications_user")
            .HasDatabaseName("ix_notifications_user")
            .IsDescending(false, true);
        builder.HasIndex(n => n.ScheduledAt, "ix_notifications_queue")
            .HasDatabaseName("ix_notifications_queue")
            .HasFilter($"status = '{NotificationStatus.Queued}'");
    }
}
