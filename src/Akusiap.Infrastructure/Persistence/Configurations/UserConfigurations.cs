using Akusiap.Domain.Auditing;
using Akusiap.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Akusiap.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", t =>
        {
            t.HasCheckConstraint(
                "ck_users_preferred_language", $"preferred_language IN ('{Languages.Indonesian}', '{Languages.English}')");
            t.HasCheckConstraint("ck_users_deactivated_at", "is_active OR deactivated_at IS NOT NULL");
        });
        builder.Property(u => u.Email).IsRequired().HasColumnType("text");
        // Case-insensitive uniqueness is the ux_users_email index on lower(email), created in the migration
        // because EF can't model expression indexes.
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.Property(r => r.Name).IsRequired().HasColumnType("text");
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.Changes).HasColumnType("jsonb");
        builder.HasOne(a => a.ActorUser).WithMany().HasForeignKey(a => a.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(a => new { a.EntityName, a.EntityId, a.CreatedAt }, "ix_audit_logs_entity")
            .HasDatabaseName("ix_audit_logs_entity")
            .IsDescending(false, false, true);
        builder.HasIndex(a => new { a.ActorUserId, a.CreatedAt }, "ix_audit_logs_actor")
            .HasDatabaseName("ix_audit_logs_actor")
            .IsDescending(false, true);
    }
}
