using System.Globalization;
using System.Net;
using Akusiap.Domain;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Memorization;

/// <summary>SRS-02: sets take due cards first, then new ones; a card rated Easy waits out its interval (test clock).</summary>
[Collection(UsesDatabase.Name)]
public sealed class ScheduledReviewTests(PostgresFixture database)
{
    [Fact]
    public async Task A_card_rated_easy_is_not_due_again_until_its_interval_passes()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long examId, questionId;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
            (examId, questionId) = (exam.Id, exam.Questions.Single().QuestionId);
        }

        var set = await StartAsync(learner, examId);
        using (var rated = await RateAsync(learner, set!, 1, ReviewRating.Easy, htmx: true))
        {
            var html = await rated.Content.ReadAsStringAsync();
            var due = await DueAtAsync(user.Id, questionId);
            Assert.Contains("data-testid=\"next-due\"", html, StringComparison.Ordinal);
            Assert.Contains(JakartaTime.ToLocal(due).ToString("d MMM yyyy", CultureInfo.GetCultureInfo("id-ID")), html, StringComparison.Ordinal);
            Assert.Contains("data-testid=\"flashcard-summary\"", html, StringComparison.Ordinal);
        }

        var dueAt = await DueAtAsync(user.Id, questionId);
        var tooSoon = await StartAsync(learner, examId);
        app.Clock.Advance(dueAt - app.Clock.UtcNow - TimeSpan.FromMinutes(1));
        var stillTooSoon = await StartAsync(learner, examId);
        app.Clock.Advance(TimeSpan.FromMinutes(2));
        var due2 = await StartAsync(learner, examId);

        Assert.Null(tooSoon);
        Assert.Null(stillTooSoon);
        Assert.NotNull(due2);
    }

    private async Task<DateTime> DueAtAsync(Guid userId, long questionId)
    {
        await using var db = database.CreateContext();
        return (await db.ReviewCards.SingleAsync(c => c.UserId == userId && c.QuestionId == questionId)).DueAt;
    }

    /// <summary>The set URL, or null when the deck had nothing to show (back to the deck list).</summary>
    private static async Task<string?> StartAsync(HttpClient client, long examId)
    {
        using var response = await client.PostFormAsync("/learn/kartu", new Dictionary<string, string> { ["examId"] = examId.ToString(CultureInfo.InvariantCulture) }, "/learn/kartu?handler=Start");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        return location == "/learn/kartu" ? null : location;
    }

    private static async Task<HttpResponseMessage> RateAsync(HttpClient client, string set, int position, ReviewRating rating, bool htmx)
    {
        var token = await client.GetAntiforgeryTokenAsync(set);
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{set}?handler=Rate", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["position"] = position.ToString(CultureInfo.InvariantCulture),
                ["rating"] = rating.ToString(),
            }),
        };
        if (htmx)
        {
            request.Headers.Add("HX-Request", "true");
        }

        return await client.SendAsync(request);
    }
}
