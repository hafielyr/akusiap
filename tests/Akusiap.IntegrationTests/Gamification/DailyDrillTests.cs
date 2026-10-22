using System.Globalization;
using System.Net;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Gamification;

/// <summary>DRL-01: a 10-question daily drill from accessible questions (due cards first), with a streak in Jakarta days.</summary>
[Collection(UsesDatabase.Name)]
public sealed class DailyDrillTests(PostgresFixture database)
{
    [Fact]
    public async Task The_drill_uses_only_accessible_questions_and_due_cards_come_first()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        List<long> freeQuestions, paidQuestions;
        await using (var db = database.CreateContext())
        {
            var free = await db.SaveAsync(TestData.Exam().Free().WithQuestions(12).Build());
            var paid = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(5, trial: 5).Build());
            freeQuestions = [.. free.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId)];
            paidQuestions = [.. paid.Questions.Select(q => q.QuestionId)];
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = freeQuestions[11], State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-1) });
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = paidQuestions[0], State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-2) });
            await db.SaveChangesAsync();
        }

        var player = await StartAsync(learner);
        var again = await StartAsync(learner);

        Assert.Equal(player, again); // an unfinished drill is continued, not replaced
        await using var check = database.CreateContext();
        var attempt = await check.Attempts.Include(a => a.Items).SingleAsync(a => a.Id == IdOf(player));
        Assert.Equal((AttemptSource.DailyDrill, AttemptMode.Practice, (long?)null), (attempt.Source, attempt.Mode, attempt.ExamId));
        Assert.Equal(10, attempt.Items.Count);
        Assert.Equal(freeQuestions[11], attempt.Items.Single(i => i.Position == 1).QuestionId);
        Assert.DoesNotContain(attempt.Items, i => paidQuestions.Contains(i.QuestionId));
    }

    [Fact]
    public async Task The_drill_streak_grows_each_day_and_resets_after_a_missed_day()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(TestData.Exam().Free().WithQuestions(10).Build());
        }

        var before = await learner.GetStringAsync(new Uri("/learn/hari-ini", UriKind.Relative));
        await FinishDrillAsync(learner);
        var day1 = await learner.GetStringAsync(new Uri("/learn/hari-ini", UriKind.Relative));
        app.Clock.Advance(TimeSpan.FromDays(1));
        var nextMorning = await learner.GetStringAsync(new Uri("/learn/hari-ini", UriKind.Relative));
        await FinishDrillAsync(learner);
        var day2 = await learner.GetStringAsync(new Uri("/learn/hari-ini", UriKind.Relative));
        app.Clock.Advance(TimeSpan.FromDays(2));
        var afterGap = await learner.GetStringAsync(new Uri("/learn/hari-ini", UriKind.Relative));

        Assert.Contains("data-state=\"NotStarted\"", before, StringComparison.Ordinal);
        Assert.Contains("data-state=\"Done\"", day1, StringComparison.Ordinal);
        Assert.Contains("🔥 1 hari", day1, StringComparison.Ordinal);
        Assert.Contains("data-state=\"NotStarted\"", nextMorning, StringComparison.Ordinal);
        Assert.Contains("🔥 1 hari", nextMorning, StringComparison.Ordinal); // still alive until a whole day is missed
        Assert.Contains("🔥 2 hari", day2, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"drill-streak\">🔥 0 hari", afterGap, StringComparison.Ordinal);
        Assert.Equal(2, CountDone(day2));
    }

    private static int CountDone(string html) =>
        html.Split("data-done=\"true\"", StringSplitOptions.None).Length - 1;

    private static long IdOf(string player) => long.Parse(player.Split('/')[^1], CultureInfo.InvariantCulture);

    private static async Task<string> StartAsync(HttpClient client)
    {
        using var response = await client.PostFormAsync("/learn/hari-ini", new Dictionary<string, string>(), "/learn/hari-ini?handler=Drill");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/sesi/", location, StringComparison.Ordinal);
        return location;
    }

    private static async Task FinishDrillAsync(HttpClient client)
    {
        var player = await StartAsync(client);
        using var submitted = await client.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
    }
}
