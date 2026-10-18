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
        builder.Property(u => u.Id).HasColumnName("user_id");
        builder.Property(u => u.Email).IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();
        // Identity's lookup index on the normalized (upper-case) email, made unique so case variants can't register twice.
        builder.HasIndex(u => u.NormalizedEmail).HasDatabaseName("email_index").IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(100);
        builder.Property(u => u.PreferredLanguage).HasMaxLength(10);
    }
}

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.Property(r => r.Id).HasColumnName("role_id");
        builder.Property(r => r.Name).IsRequired();
        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.Action).HasMaxLength(100);
        builder.Property(a => a.EntityName).HasMaxLength(100);
        builder.Property(a => a.EntityId).HasMaxLength(100);
        builder.Property(a => a.ChangesJson).HasColumnType("jsonb");
        builder.HasOne(a => a.ActorUser).WithMany().HasForeignKey(a => a.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.EntityName, a.EntityId });
    }
}
