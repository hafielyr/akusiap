using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Seeding;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Seeding;

/// <summary>F-07: each test gets its own empty database so row counts are exact.</summary>
public sealed class DataSeederTests
{
    private const string AdminEmail = "admin@akusiap.test";
    private const string AdminPassword = "Rahasia-Admin-2026";

    [Fact]
    public async Task Development_startup_migrates_and_seeds_and_seeding_again_adds_nothing()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var app = StartApp(database, "Development", withAdmin: true);
        using var client = app.CreateClient();

        var before = await CountRowsAsync(database);
        await SeedAgainAsync(app, includeSampleContent: true);
        var after = await CountRowsAsync(database);

        Assert.Equal(new RowCounts(Roles: 3, Plans: 4, DefaultPresets: 1, Users: 1, Subjects: 1, Exams: 2, Questions: 50), before);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task Plans_preset_and_sample_exams_match_the_seed_values()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var app = StartApp(database, "Development", withAdmin: false);
        using var client = app.CreateClient();
        await using var db = database.CreateContext();

        var plans = await db.Plans.ToDictionaryAsync(p => p.Code);
        Assert.Equal(("Gratis", 50, 1, 50), (plans[PlanCode.Free].Name, plans[PlanCode.Free].MaxOwnQuestions, plans[PlanCode.Free].MaxOwnExams, plans[PlanCode.Free].MaxImportRows));
        Assert.True(plans[PlanCode.Free].IncludesAuthoring && plans[PlanCode.Free].IncludesClasses && !plans[PlanCode.Free].IncludesAllExams);
        Assert.All(
            plans.Values.Where(p => p.Code != PlanCode.Free),
            p => Assert.True(p.MaxOwnQuestions is null && p.MaxOwnExams is null && p.MaxImportRows is null, $"{p.Code} caps stay unset"));
        Assert.Equal("Paket Pelajar", plans[PlanCode.Learner].Name);
        Assert.True(plans[PlanCode.Learner].IncludesAllExams && plans[PlanCode.Learner].IncludesAuthoring && !plans[PlanCode.Learner].IncludesClasses);
        Assert.True(!plans[PlanCode.Teacher].IncludesAllExams && plans[PlanCode.Teacher].IncludesAuthoring && plans[PlanCode.Teacher].IncludesClasses);
        Assert.True(plans[PlanCode.Combined].IncludesAllExams && plans[PlanCode.Combined].IncludesAuthoring && plans[PlanCode.Combined].IncludesClasses);

        var preset = await db.RulePresets.SingleAsync(r => r.OwnerUserId == null && r.IsDefault);
        Assert.Equal(DataSeeder.DefaultPresetName, preset.Name);
        Assert.Equal(ShowAnswers.AfterEach, preset.ShowAnswers);
        Assert.True(preset.HintsEnabled && preset.ShuffleQuestions && preset.ShuffleOptions);
        Assert.Null(preset.TimeLimitMinutes);

        var paid = await db.Exams
            .Include(e => e.Questions).ThenInclude(eq => eq.Question!).ThenInclude(q => q.Options)
            .SingleAsync(e => !e.IsFree);
        Assert.Equal((ExamStatus.Published, 49_000L, 30), (paid.Status, paid.PriceIdr!.Value, paid.AccessDays!.Value));
        Assert.Equal(40, paid.Questions.Count);
        Assert.Equal(30, paid.Questions.Count(q => q.IsTrial));
        Assert.All(paid.Questions, eq => Assert.Single(eq.Question!.Options, o => o.IsCorrect));
        var free = await db.Exams.Include(e => e.Questions).SingleAsync(e => e.IsFree);
        Assert.Equal((ExamStatus.Published, (long?)null), (free.Status, free.PriceIdr));
        Assert.NotEmpty(free.Questions);
    }

    [Fact]
    public async Task Seeded_admin_can_sign_in_with_the_configured_password()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var app = StartApp(database, "Development", withAdmin: true);
        using var client = app.CreateClient();
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        var admin = await users.FindByEmailAsync(AdminEmail);

        Assert.NotNull(admin);
        Assert.True(admin.IsActive && admin.EmailConfirmed);
        Assert.True(await users.CheckPasswordAsync(admin, AdminPassword));
        Assert.False(await users.CheckPasswordAsync(admin, "salah"));
        Assert.Equal([RoleNames.Admin], await users.GetRolesAsync(admin));
    }

    [Fact]
    public async Task Outside_development_there_is_no_sample_content_and_no_admin_without_settings()
    {
        await using var database = await TestDatabase.CreateMigratedAsync();
        await using var app = StartApp(database, "Production", withAdmin: false);
        using var client = app.CreateClient();
        await using var db = database.CreateContext();

        Assert.Equal(3, await db.Roles.CountAsync());
        Assert.Equal(4, await db.Plans.CountAsync());
        Assert.Equal(0, await db.Users.CountAsync());
        Assert.Equal(0, await db.Exams.CountAsync());
    }

    private static WebApplicationFactory<Program> StartApp(TestDatabase database, string environment, bool withAdmin) =>
        new AppFactory(database.ConnectionString).WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.UseSetting("Seed:OnStartup", "true");
            builder.UseSetting("Seed:AdminEmail", withAdmin ? AdminEmail : string.Empty);
            builder.UseSetting("Seed:AdminPassword", withAdmin ? AdminPassword : string.Empty);
        });

    private static async Task SeedAgainAsync(WebApplicationFactory<Program> app, bool includeSampleContent)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DataSeeder>().SeedAsync(includeSampleContent, CancellationToken.None);
    }

    private static async Task<RowCounts> CountRowsAsync(TestDatabase database)
    {
        await using var db = database.CreateContext();
        return new RowCounts(
            await db.Roles.CountAsync(),
            await db.Plans.CountAsync(),
            await db.RulePresets.CountAsync(r => r.OwnerUserId == null && r.IsDefault),
            await db.Users.CountAsync(),
            await db.Subjects.CountAsync(),
            await db.Exams.CountAsync(),
            await db.Questions.CountAsync());
    }

    private sealed record RowCounts(int Roles, int Plans, int DefaultPresets, int Users, int Subjects, int Exams, int Questions);
}
