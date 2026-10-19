using System.Text.RegularExpressions;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;

namespace Akusiap.IntegrationTests.Sessions;

/// <summary>SES-10: the history lists only the user's own attempts, newest first, paged, with "Lanjutkan" for open ones.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class HistoryTests(PostgresFixture database)
{
    [Fact]
    public async Task Only_own_attempts_are_listed_and_open_ones_show_continue()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var me = TestData.User().Build();
        var someoneElse = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(me, RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(someoneElse, RoleNames.Learner);
        long finished, open, theirs;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
            var done = Attempt(me.Id, exam.Id, TestData.Now.AddHours(-2), AttemptStatus.Submitted);
            done.ScorePercent = 75m;
            done.CorrectCount = 3;
            done.TotalCount = 4;
            done.TimeSpentSeconds = 125;
            var started = Attempt(me.Id, exam.Id, TestData.Now.AddHours(-1), AttemptStatus.InProgress);
            var foreign = Attempt(someoneElse.Id, exam.Id, TestData.Now, AttemptStatus.Submitted);
            db.Attempts.AddRange(done, started, foreign);
            await db.SaveChangesAsync();
            (finished, open, theirs) = (done.Id, started.Id, foreign.Id);
        }

        var html = await learner.GetStringAsync(new Uri("/learn/riwayat", UriKind.Relative));

        Assert.Equal([open, finished], RowPattern().Matches(html).Select(m => long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Contains($"href=\"/sesi/{open}\" class=\"btn btn-primary w-full sm:w-auto\" data-testid=\"history-continue\">Lanjutkan sesi<", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/sesi/{finished}/hasil\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/sesi/{finished}/pembahasan\"", html, StringComparison.Ordinal);
        Assert.Contains("75%", html, StringComparison.Ordinal);
        Assert.Contains("2 mnt 5 dtk", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-attempt=\"{theirs}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_history_is_paged()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var me = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(me, RoleNames.Learner);
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
            for (var i = 0; i < 21; i++)
            {
                db.Attempts.Add(Attempt(me.Id, exam.Id, TestData.Now.AddMinutes(-i), AttemptStatus.Submitted));
            }

            await db.SaveChangesAsync();
        }

        var first = await learner.GetStringAsync(new Uri("/learn/riwayat", UriKind.Relative));
        var second = await learner.GetStringAsync(new Uri("/learn/riwayat?halaman=2", UriKind.Relative));

        Assert.Equal(20, RowPattern().Count(first));
        Assert.Contains("href=\"/learn/riwayat?halaman=2\"", first, StringComparison.Ordinal);
        Assert.Equal(1, RowPattern().Count(second));
    }

    private static Attempt Attempt(Guid userId, long examId, DateTime startedAt, AttemptStatus status) => new()
    {
        UserId = userId,
        ExamId = examId,
        Source = AttemptSource.Exam,
        Mode = AttemptMode.Practice,
        Status = status,
        RulesSnapshot = "{}",
        StartedAt = startedAt,
        LastSavedAt = startedAt,
        SubmittedAt = status == AttemptStatus.InProgress ? null : startedAt.AddMinutes(10),
    };

    [GeneratedRegex("data-testid=\"history-row\" data-attempt=\"(\\d+)\"")]
    private static partial Regex RowPattern();
}
