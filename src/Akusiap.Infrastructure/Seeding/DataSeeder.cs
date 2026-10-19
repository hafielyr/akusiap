using Akusiap.Application.Abstractions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Akusiap.Infrastructure.Seeding;

/// <summary>
/// Inserts the reference data the app needs (roles, plans, the default rule preset, the admin) and, in Development,
/// sample content. Insert-only and idempotent: rows that already exist are left as they are, so admin edits survive.
/// </summary>
public sealed partial class DataSeeder(
    AppDbContext db,
    RoleManager<Role> roleManager,
    UserManager<User> userManager,
    IClock clock,
    IOptions<SeedOptions> options,
    ILogger<DataSeeder> logger)
{
    public const string DefaultPresetName = "Latihan standar";

    /// <summary>Plans from the schema.sql seed. Paid-plan caps stay null ("not configured", treated as blocked) until decided.</summary>
    private static readonly Plan[] Plans =
    [
        new() { Code = PlanCode.Free, Name = "Gratis", IncludesAuthoring = true, IncludesClasses = true, MaxOwnQuestions = 50, MaxOwnExams = 1, MaxImportRows = 50 },
        new() { Code = PlanCode.Learner, Name = "Paket Pelajar", IncludesAllExams = true, IncludesAuthoring = true },
        new() { Code = PlanCode.Teacher, Name = "Paket Pengajar", IncludesAuthoring = true, IncludesClasses = true },
        new() { Code = PlanCode.Combined, Name = "Paket Lengkap", IncludesAllExams = true, IncludesAuthoring = true, IncludesClasses = true },
    ];

    public async Task SeedAsync(bool includeSampleContent, CancellationToken cancellationToken)
    {
        await SeedRolesAsync();
        await SeedPlansAsync(cancellationToken);
        var preset = await SeedDefaultPresetAsync(cancellationToken);
        await SeedAdminAsync(cancellationToken);
        if (includeSampleContent)
        {
            await SampleContent.SeedAsync(db, preset, clock.UtcNow, cancellationToken);
        }
    }

    private async Task SeedRolesAsync()
    {
        foreach (var name in RoleNames.All)
        {
            if (!await roleManager.RoleExistsAsync(name))
            {
                ThrowIfFailed(await roleManager.CreateAsync(new Role(name)), $"create role {name}");
            }
        }
    }

    private async Task SeedPlansAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Plans.Select(p => p.Code).ToListAsync(cancellationToken);
        foreach (var plan in Plans.Where(p => !existing.Contains(p.Code)))
        {
            db.Plans.Add(new Plan
            {
                Code = plan.Code,
                Name = plan.Name,
                IncludesAllExams = plan.IncludesAllExams,
                IncludesAuthoring = plan.IncludesAuthoring,
                IncludesClasses = plan.IncludesClasses,
                MaxOwnQuestions = plan.MaxOwnQuestions,
                MaxOwnExams = plan.MaxOwnExams,
                MaxImportRows = plan.MaxImportRows,
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<RulePreset> SeedDefaultPresetAsync(CancellationToken cancellationToken)
    {
        var preset = await db.RulePresets.SingleOrDefaultAsync(r => r.OwnerUserId == null && r.IsDefault, cancellationToken);
        if (preset is null)
        {
            preset = new RulePreset
            {
                Name = DefaultPresetName,
                HintsEnabled = true,
                ShuffleQuestions = true,
                ShuffleOptions = true,
                ShowAnswers = ShowAnswers.AfterEach,
                IsDefault = true,
            };
            db.RulePresets.Add(preset);
            await db.SaveChangesAsync(cancellationToken);
        }

        return preset;
    }

    private async Task SeedAdminAsync(CancellationToken cancellationToken)
    {
        var email = options.Value.AdminEmail;
        var password = options.Value.AdminPassword;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            LogAdminSkipped();
            return;
        }

        var admin = await userManager.FindByEmailAsync(email);
        if (admin is null)
        {
            admin = new User
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = "Admin",
                CreatedAt = clock.UtcNow,
            };
            ThrowIfFailed(await userManager.CreateAsync(admin, password), "create the admin user");
            LogAdminCreated(email);
        }

        if (!await userManager.IsInRoleAsync(admin, RoleNames.Admin))
        {
            ThrowIfFailed(await userManager.AddToRoleAsync(admin, RoleNames.Admin), "add the admin role");
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void ThrowIfFailed(IdentityResult result, string action)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Seeding could not {action}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Seed:AdminEmail or Seed:AdminPassword is not set, so no admin user was seeded.")]
    private partial void LogAdminSkipped();

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded the admin user {Email}.")]
    private partial void LogAdminCreated(string email);
}
