using Akusiap.Domain.Planning;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Database;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Persistence;

/// <summary>PLN-01: study_goals with one active goal per user and exam (D-120).</summary>
public sealed class StudyGoalsMigrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 7, 2, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, string[]> Columns = new()
    {
        ["study_goals"] =
        [
            "id int8", "user_id uuid", "exam_id int8", "exam_date date", "created_at timestamptz", "updated_at timestamptz",
            "archived_at timestamptz?",
        ],
    };

    private TestDatabase _database = null!;

    public async Task InitializeAsync() => _database = await TestDatabase.CreateMigratedAsync();

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task The_table_has_the_columns()
    {
        await using var db = _database.CreateContext();

        Assert.Empty(await SchemaAssertions.FindColumnProblemsAsync(db, Columns));
    }

    [Fact]
    public async Task Only_one_goal_per_exam_can_be_active()
    {
        await using var db = _database.CreateContext();
        var user = new User { UserName = "target@contoh.id", Email = "target@contoh.id", DisplayName = "Target", CreatedAt = Now };
        var exam = Akusiap.TestSupport.Builders.TestData.Exam().Free().WithQuestions(1).Build();
        db.Users.Add(user);
        db.Exams.Add(exam);
        var date = new DateOnly(2027, 3, 2);
        db.StudyGoals.AddRange(
            new StudyGoal { User = user, Exam = exam, ExamDate = date, CreatedAt = Now, UpdatedAt = Now, ArchivedAt = Now },
            new StudyGoal { User = user, Exam = exam, ExamDate = date, CreatedAt = Now, UpdatedAt = Now });
        await db.SaveChangesAsync();

        db.StudyGoals.Add(new StudyGoal { UserId = user.Id, ExamId = exam.Id, ExamDate = date, CreatedAt = Now, UpdatedAt = Now });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
