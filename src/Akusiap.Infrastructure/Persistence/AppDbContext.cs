using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Auditing;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<User, Role, Guid>(options), IAppDbContext
{
    /// <summary>Max length of columns that hold enum names.</summary>
    public const int EnumMaxLength = 32;

    public DbSet<Subject> Subjects => Set<Subject>();

    public DbSet<Topic> Topics => Set<Topic>();

    public DbSet<Question> Questions => Set<Question>();

    public DbSet<QuestionOption> QuestionOptions => Set<QuestionOption>();

    public DbSet<Exam> Exams => Set<Exam>();

    public DbSet<ExamQuestion> ExamQuestions => Set<ExamQuestion>();

    public DbSet<RulePreset> RulePresets => Set<RulePreset>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<PlanPrice> PlanPrices => Set<PlanPrice>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<ExamPurchase> ExamPurchases => Set<ExamPurchase>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Identity tables get plain snake_case names instead of asp_net_*.
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        AddEnumCheckConstraints(builder);
    }

    /// <summary>Adds <c>CHECK (column IN (...))</c> for every enum column so the database rejects unknown names.</summary>
    private static void AddEnumCheckConstraints(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes().ToList())
        {
            var table = entityType.GetTableName();
            if (table is null)
            {
                continue;
            }

            foreach (var property in entityType.GetDeclaredProperties())
            {
                var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!enumType.IsEnum)
                {
                    continue;
                }

                var column = property.GetColumnName();
                var values = string.Join(", ", Enum.GetNames(enumType).Select(name => $"'{name}'"));
                builder.Entity(entityType.ClrType).ToTable(t => t.HasCheckConstraint($"ck_{table}_{column}", $"{column} IN ({values})"));
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Every domain enum is stored as its name (architecture.md).
        var enumTypes = typeof(User).Assembly.GetTypes().Where(t => t.IsEnum && t.IsPublic);
        foreach (var enumType in enumTypes)
        {
            configurationBuilder.Properties(enumType).HaveConversion<string>().HaveMaxLength(EnumMaxLength);
        }
    }
}
