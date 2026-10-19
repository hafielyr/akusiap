using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Fakes;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Sessions;

/// <summary>SES-09: "Ulangi yang salah" starts a WrongOnly attempt with exactly the wrong questions, within today's access.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class RetakeTests(PostgresFixture database)
{
    [Fact]
    public async Task The_new_attempt_contains_exactly_the_wrong_questions()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Ulang " + TestData.Unique(), ShuffleQuestions = false, ShuffleOptions = false };
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(4).Build())).Id;
        }

        var player = await StartAsync(learner, examId);
        var (token, items) = await StateAsync(learner, player);
        await SaveAsync(learner, player, token, 1, Option(items[0], 0)); // right
        await SaveAsync(learner, player, token, 2, Option(items[1], 1)); // wrong
        await SaveAsync(learner, player, token, 3, Option(items[2], 2)); // wrong; 4 stays blank
        using (await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit"))
        {
        }

        var result = await learner.GetStringAsync(new Uri($"{player}/hasil", UriKind.Relative));
        var review = await learner.GetStringAsync(new Uri($"{player}/pembahasan", UriKind.Relative));
        using var retake = await learner.PostFormAsync($"{player}/hasil", new Dictionary<string, string>(), $"{player}/ulangi");

        Assert.Contains("data-testid=\"retake-wrong\"", result, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"retake-wrong\"", review, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, retake.StatusCode);
        var newAttempt = await LoadAsync(retake.Headers.Location!.OriginalString);
        var source = await LoadAsync(player);
        Assert.Equal((AttemptSource.WrongOnly, AttemptMode.Practice, AttemptStatus.InProgress), (newAttempt.Source, newAttempt.Mode, newAttempt.Status));
        Assert.Equal(
            source.Items.Where(i => i.Position > 1).Select(i => i.QuestionId).Order(),
            newAttempt.Items.Select(i => i.QuestionId).Order());
    }

    [Fact]
    public async Task A_retake_after_access_ended_stays_within_the_trial()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var exam = TestData.Exam().Paid(49_000).WithQuestions(4, trial: 2).Build();
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(exam)).Id;
            db.ExamPurchases.Add(new ExamPurchase
            {
                UserId = user.Id, ExamId = examId, PricePaidIdr = 49_000, Status = ExamPurchaseStatus.Active,
                PurchasedAt = TestClock.DefaultStart.AddDays(-1), AccessExpiresAt = TestClock.DefaultStart.AddDays(1),
            });
            await db.SaveChangesAsync();
        }

        var player = await StartAsync(learner, examId);
        using (await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit"))
        {
        }

        app.Clock.Advance(TimeSpan.FromDays(2));
        using var retake = await learner.PostFormAsync($"{player}/hasil", new Dictionary<string, string>(), $"{player}/ulangi");

        Assert.Equal(HttpStatusCode.Redirect, retake.StatusCode);
        var source = await LoadAsync(player);
        var newAttempt = await LoadAsync(retake.Headers.Location!.OriginalString);
        var trialIds = exam.Questions.Where(q => q.IsTrial).Select(q => q.QuestionId).Order().ToList();
        Assert.Equal(4, source.Items.Count);
        Assert.Equal(trialIds, newAttempt.Items.Select(i => i.QuestionId).Order());
    }

    [Fact]
    public async Task Strangers_cannot_retake_someone_elses_attempt()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(2).Build())).Id;
        }

        var player = await StartAsync(learner, examId);
        using (await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit"))
        {
        }

        using var stranger = await other.PostFormAsync("/learn/profil", new Dictionary<string, string>(), $"{player}/ulangi");

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
    }

    private static long Option(JsonElement item, int index) => item.GetProperty("options")[index].GetProperty("id").GetInt64();

    private async Task<Attempt> LoadAsync(string playerUrl)
    {
        var id = long.Parse(playerUrl.Split('/')[^1], CultureInfo.InvariantCulture);
        await using var db = database.CreateContext();
        return await db.Attempts.Include(a => a.Items).SingleAsync(a => a.Id == id);
    }

    private static async Task<string> StartAsync(HttpClient client, long examId)
    {
        using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", new Dictionary<string, string>
        {
            ["Form.Mode"] = "Practice", ["Form.Untimed"] = "true", ["Form.ShuffleQuestions"] = "false", ["Form.ShuffleOptions"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }

    private static async Task<(string Token, List<JsonElement> Items)> StateAsync(HttpClient client, string player)
    {
        var html = await client.GetStringAsync(new Uri(player, UriKind.Relative));
        var state = JsonDocument.Parse(StatePattern().Match(html).Groups[1].Value).RootElement;
        return (state.GetProperty("token").GetString()!, [.. state.GetProperty("items").EnumerateArray()]);
    }

    private static async Task SaveAsync(HttpClient client, string player, string token, int position, long option)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{player}/jawab", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["position"] = position.ToString(CultureInfo.InvariantCulture),
                ["selected"] = option.ToString(CultureInfo.InvariantCulture),
                ["flagged"] = "false",
                ["seconds"] = "4",
            }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [GeneratedRegex("<script type=\"application/json\" id=\"quiz-state\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex StatePattern();
}
