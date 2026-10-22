using Akusiap.Domain.Classes;
using Akusiap.Domain.Gamification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Akusiap.Infrastructure.Persistence.Configurations;

/// <summary>Classes from schema.sql (GAM-01): classrooms, class_members and class_assignments.</summary>
public sealed class ClassroomConfiguration : IEntityTypeConfiguration<Classroom>
{
    public void Configure(EntityTypeBuilder<Classroom> builder)
    {
        builder.HasOne(c => c.TeacherUser).WithMany().HasForeignKey(c => c.TeacherUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(c => c.TeacherUserId, "ix_classrooms_teacher").HasDatabaseName("ix_classrooms_teacher");
        builder.HasIndex(c => c.InviteCode).IsUnique();
        builder.HasMany(c => c.Members).WithOne(m => m.Classroom).HasForeignKey(m => m.ClassroomId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Assignments).WithOne(a => a.Classroom).HasForeignKey(a => a.ClassroomId).OnDelete(DeleteBehavior.NoAction);
    }
}

public sealed class ClassMemberConfiguration : IEntityTypeConfiguration<ClassMember>
{
    public void Configure(EntityTypeBuilder<ClassMember> builder)
    {
        builder.HasKey(m => new { m.ClassroomId, m.UserId });
        builder.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(m => m.UserId, "ix_class_members_user").HasDatabaseName("ix_class_members_user");
    }
}

public sealed class ClassAssignmentConfiguration : IEntityTypeConfiguration<ClassAssignment>
{
    public void Configure(EntityTypeBuilder<ClassAssignment> builder)
    {
        builder.ToTable(t => t.HasCheckConstraint("ck_class_assignments_window", "closes_at > opens_at"));
        builder.HasOne(a => a.Exam).WithMany().HasForeignKey(a => a.ExamId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(a => a.RulePreset).WithMany().HasForeignKey(a => a.RulePresetId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(a => a.ClassroomId, "ix_class_assignments_classroom").HasDatabaseName("ix_class_assignments_classroom");
        builder.HasIndex(a => a.ExamId, "ix_class_assignments_exam").HasDatabaseName("ix_class_assignments_exam");
        builder.HasIndex(a => a.RulePresetId, "ix_class_assignments_rule_preset").HasDatabaseName("ix_class_assignments_rule_preset");
    }
}

/// <summary>Earned badges (GAM-01, D-95): one per code, and per exam for rank badges, and per exam and week for weekly ones (DRL-02).</summary>
public sealed class UserBadgeConfiguration : IEntityTypeConfiguration<UserBadge>
{
    public void Configure(EntityTypeBuilder<UserBadge> builder)
    {
        builder.HasOne(b => b.User).WithMany().HasForeignKey(b => b.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(b => b.Exam).WithMany().HasForeignKey(b => b.ExamId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(b => new { b.UserId, b.Code, b.ExamId, b.WeekStart }, "ux_user_badges_once")
            .HasDatabaseName("ux_user_badges_once")
            .IsUnique()
            .AreNullsDistinct(false);
    }
}

/// <summary>Per-exam leaderboard settings (DRL-02, D-99): one row per exam, keyed by the exam.</summary>
public sealed class LeaderboardSettingsConfiguration : IEntityTypeConfiguration<LeaderboardSettings>
{
    public void Configure(EntityTypeBuilder<LeaderboardSettings> builder)
    {
        builder.ToTable("leaderboard_settings", t =>
        {
            t.HasCheckConstraint("ck_leaderboard_settings_min_score", "min_score_percent IS NULL OR min_score_percent BETWEEN 0 AND 100");
            t.HasCheckConstraint("ck_leaderboard_settings_min_participants", "min_participants IS NULL OR min_participants >= 1");
        });
        builder.HasKey(s => s.ExamId);
        builder.Property(s => s.ExamId).ValueGeneratedNever();
        builder.HasOne(s => s.Exam).WithOne().HasForeignKey<LeaderboardSettings>(s => s.ExamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Domain.Users.User>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.SetNull);
    }
}
