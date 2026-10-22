using Akusiap.Domain.Gamification;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Database;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Persistence;

/// <summary>DRL-02: leaderboard_settings and user_badges.week_start (D-99).</summary>
public sealed class LeaderboardSettingsMigrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 22, 2, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string[]> Columns = new()
    {
        ["leaderboard_settings"] =
        [
            "exam_id int8", "enabled bool", "weekly_reset bool", "award_rank1 bool", "award_top3 bool", "award_top10 bool",
            "min_score_percent int4?", "min_participants int4?", "counts_since timestamptz?", "weekly_awarded_through date?",
            "updated_at timestamptz", "updated_by_user_id uuid?",
        ],
        ["user_badges"] = ["week_start date?"],
    };

    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task The_tables_have_the_columns()
    {
        await using var db = _database.CreateContext();

        Assert.Empty(await SchemaAssertions.FindColumnProblemsAsync(db, Columns));
    }

    [Fact]
    public async Task A_weekly_rank_badge_can_be_won_once_per_week()
    {
        await using var db = _database.CreateContext();
        var user = new User { UserName = "mingguan@contoh.id", Email = "mingguan@contoh.id", DisplayName = "Mingguan", CreatedAt = Now };
        var exam = Akusiap.TestSupport.Builders.TestData.Exam().Free().WithQuestions(1).Build();
        db.Users.Add(user);
        db.Exams.Add(exam);
        var monday = new DateOnly(2026, 10, 19);
        db.UserBadges.AddRange(
            new UserBadge { User = user, Code = BadgeCode.Rank1, Exam = exam, WeekStart = monday, AwardedAt = Now },
            new UserBadge { User = user, Code = BadgeCode.Rank1, Exam = exam, WeekStart = monday.AddDays(7), AwardedAt = Now },
            new UserBadge { User = user, Code = BadgeCode.Rank1, Exam = exam, AwardedAt = Now });
        await db.SaveChangesAsync();

        db.UserBadges.Add(new UserBadge { UserId = user.Id, Code = BadgeCode.Rank1, ExamId = exam.Id, WeekStart = monday, AwardedAt = Now });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
