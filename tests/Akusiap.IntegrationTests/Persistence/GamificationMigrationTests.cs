using Akusiap.Domain.Gamification;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Database;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Persistence;

/// <summary>GAM-01: the class tables of schema.sql, plus user_badges and users.hide_on_leaderboard (D-94, D-95).</summary>
public sealed class GamificationMigrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 22, 2, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string[]> SchemaColumns = new()
    {
        ["classrooms"] = ["id int8", "teacher_user_id uuid", "name text", "invite_code text", "created_at timestamptz"],
        ["class_members"] = ["classroom_id int8", "user_id uuid", "joined_at timestamptz", "removed_at timestamptz?"],
        ["class_assignments"] = ["id int8", "classroom_id int8", "exam_id int8", "rule_preset_id int8", "opens_at timestamptz", "closes_at timestamptz"],
        ["user_badges"] = ["id int8", "user_id uuid", "code text", "exam_id int8?", "awarded_at timestamptz"],
    };

    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task The_class_and_badge_tables_match_the_schema()
    {
        await using var db = _database.CreateContext();

        Assert.Empty(await SchemaAssertions.FindColumnProblemsAsync(db, SchemaColumns));
    }

    [Fact]
    public async Task Attempts_reference_class_assignments()
    {
        await using var db = _database.CreateContext();
        var foreignKeys = await db.Database.SqlQueryRaw<string>(
            "SELECT conname AS \"Value\" FROM pg_constraint WHERE conrelid = 'attempts'::regclass AND contype = 'f'").ToListAsync();

        Assert.Contains("fk_attempts_class_assignments_class_assignment_id", foreignKeys);
    }

    [Fact]
    public async Task A_milestone_badge_can_be_earned_only_once()
    {
        await using var db = _database.CreateContext();
        var user = new User { UserName = "lencana@contoh.id", Email = "lencana@contoh.id", DisplayName = "Lencana", CreatedAt = Now };
        db.Users.Add(user);
        db.UserBadges.Add(new UserBadge { User = user, Code = BadgeCode.FirstSteps, AwardedAt = Now });
        await db.SaveChangesAsync();

        db.UserBadges.Add(new UserBadge { UserId = user.Id, Code = BadgeCode.FirstSteps, AwardedAt = Now });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
