using Akusiap.Domain.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Akusiap.Infrastructure.Persistence.Configurations;

public sealed class SubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.HasIndex(s => s.Name).IsUnique();
    }
}

public sealed class TopicConfiguration : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> builder)
    {
        builder.HasOne(t => t.Subject).WithMany(s => s.Topics).HasForeignKey(t => t.SubjectId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(t => new { t.SubjectId, t.Name }).IsUnique();
    }
}

public sealed class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_questions_difficulty", $"difficulty BETWEEN {Question.MinDifficulty} AND {Question.MaxDifficulty}"));
        builder.HasOne(q => q.OwnerUser).WithMany().HasForeignKey(q => q.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(q => q.Topic).WithMany().HasForeignKey(q => q.TopicId).OnDelete(DeleteBehavior.NoAction);
        builder.HasMany(q => q.Options).WithOne(o => o.Question).HasForeignKey(o => o.QuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(q => q.VariantGroupId).HasFilter("variant_group_id IS NOT NULL");
    }
}

public sealed class RulePresetConfiguration : IEntityTypeConfiguration<RulePreset>
{
    public void Configure(EntityTypeBuilder<RulePreset> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_rule_presets_time_limit_minutes", "time_limit_minutes > 0");
            t.HasCheckConstraint("ck_rule_presets_question_count", "question_count > 0");
            t.HasCheckConstraint("ck_rule_presets_max_attempts", "max_attempts > 0");
            t.HasCheckConstraint("ck_rule_presets_pass_mark_percent", "pass_mark_percent BETWEEN 0 AND 100");
        });
        builder.HasOne(r => r.OwnerUser).WithMany().HasForeignKey(r => r.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(r => r.IsDefault, "ux_rule_presets_one_system_default")
            .HasDatabaseName("ux_rule_presets_one_system_default")
            .IsUnique()
            .HasFilter("is_default AND owner_user_id IS NULL");
    }
}

public sealed class ExamConfiguration : IEntityTypeConfiguration<Exam>
{
    public void Configure(EntityTypeBuilder<Exam> builder)
    {
        builder.ToTable(t =>
        {
            t.HasCheckConstraint("ck_exams_price_idr", "price_idr >= 0");
            t.HasCheckConstraint("ck_exams_access_days", "access_days > 0");
            t.HasCheckConstraint("ck_exams_own_question_allowance", "own_question_allowance >= 0");
            t.HasCheckConstraint("ck_exams_owner", "(owner_kind = 'App') = (owner_user_id IS NULL)");
            t.HasCheckConstraint("ck_exams_price_app_only", "owner_kind = 'App' OR price_idr IS NULL");
            t.HasCheckConstraint("ck_exams_shared_has_token", "NOT is_shared OR share_token IS NOT NULL");
        });
        builder.HasIndex(e => e.ShareToken).IsUnique();
        builder.HasIndex(e => new { e.Status, e.SubjectId }, "ix_exams_catalog")
            .HasDatabaseName("ix_exams_catalog").HasFilter("owner_kind = 'App'");
        builder.HasOne(e => e.OwnerUser).WithMany().HasForeignKey(e => e.OwnerUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.Subject).WithMany().HasForeignKey(e => e.SubjectId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(e => e.RulePreset).WithMany().HasForeignKey(e => e.RulePresetId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ExamQuestionConfiguration : IEntityTypeConfiguration<ExamQuestion>
{
    public void Configure(EntityTypeBuilder<ExamQuestion> builder)
    {
        builder.HasKey(eq => new { eq.ExamId, eq.QuestionId });
        builder.HasOne(eq => eq.Exam).WithMany(e => e.Questions).HasForeignKey(eq => eq.ExamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(eq => eq.Question).WithMany().HasForeignKey(eq => eq.QuestionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(eq => eq.ExamId, "ix_exam_questions_trial")
            .HasDatabaseName("ix_exam_questions_trial").HasFilter("is_trial");
    }
}
