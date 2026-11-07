using Akusiap.Domain.Planning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Akusiap.Infrastructure.Persistence.Configurations;

/// <summary>Exam goals (PLN-01, D-120): one active goal per user and exam. Not in schema.sql.</summary>
public sealed class StudyGoalConfiguration : IEntityTypeConfiguration<StudyGoal>
{
    public void Configure(EntityTypeBuilder<StudyGoal> builder)
    {
        builder.HasOne(g => g.User).WithMany().HasForeignKey(g => g.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(g => g.Exam).WithMany().HasForeignKey(g => g.ExamId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(g => new { g.UserId, g.ExamId }, "ux_study_goals_active")
            .HasDatabaseName("ux_study_goals_active")
            .IsUnique()
            .HasFilter("archived_at IS NULL");
    }
}
