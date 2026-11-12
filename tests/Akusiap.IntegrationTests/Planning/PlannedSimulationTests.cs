using System.Globalization;
using System.Net;
using Akusiap.Domain;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Planning;

/// <summary>PLN-06 (D-127): the plan schedules a full Simulation every 7 days and on H-7 and H-3.</summary>
[Collection(UsesDatabase.Name)]
public sealed class PlannedSimulationTests(PostgresFixture database)
{
    private const string TodayUrl = "/learn/hari-ini";

    [Fact]
    public async Task The_simulation_shows_on_planned_days_and_is_done_once_finished_that_day()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(10).Build())).Id;
        }

        using (var saved = await learner.PostFormAsync("/learn/target", new Dictionary<string, string>
        {
            ["Input.ExamId"] = examId.ToString(CultureInfo.InvariantCulture),
            ["Input.ExamDate"] = JakartaTime.Today(app.Clock.UtcNow).AddDays(30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        }))
        {
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        }

        long goalId;
        await using (var db = database.CreateContext())
        {
            goalId = await db.StudyGoals.Where(g => g.UserId == user.Id && g.ExamId == examId).Select(g => g.Id).SingleAsync();
        }

        var firstDay = await TodayAsync(learner);
        app.Clock.Advance(TimeSpan.FromDays(7));
        var seventhDay = await TodayAsync(learner);
        using var started = await learner.PostFormAsync(TodayUrl, new Dictionary<string, string> { ["goalId"] = goalId.ToString(CultureInfo.InvariantCulture) }, $"{TodayUrl}?handler=Simulation");
        var player = started.Headers.Location!.OriginalString;
        var running = await TodayAsync(learner);
        using var submitted = await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");
        var finished = await TodayAsync(learner);
        app.Clock.Advance(TimeSpan.FromDays(1));
        var eighthDay = await TodayAsync(learner);

        Assert.DoesNotContain("data-testid=\"plan-simulation\"", firstDay, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"plan-simulation\" data-state=\"Due\"", seventhDay, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"plan-simulation-start\"", seventhDay, StringComparison.Ordinal);
        Assert.StartsWith("/sesi/", player, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"plan-simulation\" data-state=\"InProgress\"", running, StringComparison.Ordinal);
        Assert.Contains($"href=\"{player}\"", running, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        Assert.Contains("data-testid=\"plan-simulation\" data-state=\"Done\"", finished, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"plan-simulation-start\"", finished, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"plan-simulation\"", eighthDay, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        var attempt = await check.Attempts.SingleAsync(a => a.UserId == user.Id && a.Mode == AttemptMode.Simulation);
        Assert.Equal((AttemptSource.Exam, (long?)examId), (attempt.Source, attempt.ExamId));
    }

    private static Task<string> TodayAsync(HttpClient client) => client.GetStringAsync(new Uri(TodayUrl, UriKind.Relative));
}
