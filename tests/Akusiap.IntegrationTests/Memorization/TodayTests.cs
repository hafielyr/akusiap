using System.Net;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;

namespace Akusiap.IntegrationTests.Memorization;

/// <summary>SRS-03: today's due counts, per exam and in total, only for exams with access; the learner's home page.</summary>
[Collection(UsesDatabase.Name)]
public sealed class TodayTests(PostgresFixture database)
{
    [Fact]
    public async Task Counts_match_the_due_cards_and_exams_without_access_are_excluded()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long open, closed;
        await using (var db = database.CreateContext())
        {
            var free = await db.SaveAsync(TestData.Exam().Free().WithQuestions(3).Build());
            var paid = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(2, trial: 2).Build());
            (open, closed) = (free.Id, paid.Id);
            var freeQuestions = free.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId).ToList();
            var now = app.Clock.UtcNow;
            db.ReviewCards.AddRange(
                Card(user.Id, freeQuestions[0], now.AddHours(-2)),
                Card(user.Id, freeQuestions[1], now.AddMinutes(-1)),
                Card(user.Id, freeQuestions[2], now.AddDays(3)),
                Card(user.Id, paid.Questions.First().QuestionId, now.AddHours(-1)));
            await db.SaveChangesAsync();
        }

        var html = await learner.GetStringAsync(new Uri("/learn/hari-ini", UriKind.Relative));
        using var home = await learner.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Contains("data-testid=\"today-total-count\">2<", html, StringComparison.Ordinal);
        Assert.Contains($"data-exam=\"{open}\"", html, StringComparison.Ordinal);
        Assert.Contains("2 kartu jatuh tempo", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-exam=\"{closed}\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, home.StatusCode);
        Assert.Equal("/learn/hari-ini", home.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Guests_and_admins_keep_the_normal_home_page()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        using var guest = app.CreateClient();

        using var adminHome = await admin.GetAsync(new Uri("/", UriKind.Relative));
        using var guestHome = await guest.GetAsync(new Uri("/", UriKind.Relative));

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (adminHome.StatusCode, guestHome.StatusCode));
    }

    private static ReviewCard Card(Guid userId, long questionId, DateTime dueAt) =>
        new() { UserId = userId, QuestionId = questionId, State = ReviewCardState.Review, DueAt = dueAt, IntervalDays = 1, Repetitions = 2 };
}
