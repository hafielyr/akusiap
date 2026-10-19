using Akusiap.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Akusiap.Infrastructure.Persistence.Configurations;

public sealed class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
{
    public void Configure(EntityTypeBuilder<Attempt> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_attempts_time_spent_seconds", "time_spent_seconds >= 0");
            t.HasCheckConstraint("ck_attempts_correct_count", "correct_count >= 0");
            t.HasCheckConstraint("ck_attempts_total_count", "total_count >= 0");
            t.HasCheckConstraint("ck_attempts_score_percent", "score_percent BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_attempts_correct_within_total", "correct_count <= total_count");
            // Guests may only take trials, or shared exams under a name.
            t.HasCheckConstraint(
                "ck_attempts_taker",
                $"user_id IS NOT NULL OR source = '{AttemptSource.Trial}' OR (source = '{AttemptSource.Shared}' AND guest_name IS NOT NULL)");
            t.HasCheckConstraint(
                "ck_attempts_exam_required",
                $"source NOT IN ('{AttemptSource.Exam}', '{AttemptSource.Trial}', '{AttemptSource.Shared}') OR exam_id IS NOT NULL");
            t.HasCheckConstraint(
                "ck_attempts_submitted_at", $"status <> '{AttemptStatus.Submitted}' OR submitted_at IS NOT NULL");
        });
        builder.Property(a => a.RulesSnapshot).HasColumnType("jsonb");
        builder.Property(a => a.ScorePercent).HasPrecision(5, 2);
        builder.HasOne(a => a.User).WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.Exam).WithMany().HasForeignKey(a => a.ExamId).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(a => a.Items).WithOne(i => i.Attempt).HasForeignKey(i => i.AttemptId).OnDelete(DeleteBehavior.Cascade);
        // Session history (H-01).
        builder.HasIndex(a => new { a.UserId, a.StartedAt }, "ix_attempts_user")
            .HasDatabaseName("ix_attempts_user")
            .IsDescending(false, true);
        // The foreign key to class_assignments is added with that table (D-27).
        builder.HasIndex(a => a.ClassAssignmentId);
        builder.HasIndex(a => a.GuestToken, "ix_attempts_guest_token")
            .HasDatabaseName("ix_attempts_guest_token")
            .HasFilter("guest_token IS NOT NULL");
    }
}

public sealed class AttemptItemConfiguration : IEntityTypeConfiguration<AttemptItem>
{
    public void Configure(EntityTypeBuilder<AttemptItem> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("ck_attempt_items_time_spent_seconds", "time_spent_seconds >= 0"));
        builder.Property(i => i.OptionOrder).HasColumnType("jsonb");
        builder.HasOne(i => i.Question).WithMany().HasForeignKey(i => i.QuestionId).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(i => i.SelectedOptions)
            .WithOne(o => o.AttemptItem)
            .HasForeignKey(o => o.AttemptItemId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(i => new { i.AttemptId, i.Position }).IsUnique();
        builder.HasIndex(i => new { i.AttemptId, i.QuestionId }).IsUnique();
    }
}

public sealed class AttemptItemOptionConfiguration : IEntityTypeConfiguration<AttemptItemOption>
{
    public void Configure(EntityTypeBuilder<AttemptItemOption> builder)
    {
        builder.HasKey(o => new { o.AttemptItemId, o.QuestionOptionId });
        builder.HasOne(o => o.QuestionOption).WithMany().HasForeignKey(o => o.QuestionOptionId).OnDelete(DeleteBehavior.NoAction);
    }
}
