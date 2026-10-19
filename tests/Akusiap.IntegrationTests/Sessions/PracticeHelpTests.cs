using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Sessions;

/// <summary>SES-05: "Periksa" gives feedback and locks the answer; hints are recorded; Simulation gets neither.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class PracticeHelpTests(PostgresFixture database)
{
    [Fact]
    public async Task Checking_shows_feedback_locks_the_answer_and_survives_a_reload()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(ShowAnswers.AfterEach, hints: true);
        var player = await StartAsync(learner, examId, "Practice");
        var (token, items) = await StateAsync(learner, player);
        var correct = Option(items[0], 0);
        var wrong = Option(items[0], 1);
        await SaveAsync(learner, player, token, 1, wrong, HttpStatusCode.NoContent);

        using var checkedResponse = await HelpAsync(learner, player, token, "Check", 1);
        var feedback = JsonDocument.Parse(await checkedResponse.Content.ReadAsStringAsync()).RootElement;
        await SaveAsync(learner, player, token, 1, correct, HttpStatusCode.BadRequest);
        var (_, reloaded) = await StateAsync(learner, player);

        Assert.Equal(HttpStatusCode.OK, checkedResponse.StatusCode);
        Assert.False(feedback.GetProperty("isCorrect").GetBoolean());
        Assert.Equal([correct], feedback.GetProperty("correctOptionIds").EnumerateArray().Select(e => e.GetInt64()));
        Assert.Equal("Pembahasan soal nomor 1.", feedback.GetProperty("explanation").GetString());
        Assert.Equal(JsonValueKind.Object, reloaded[0].GetProperty("feedback").ValueKind);
        Assert.Equal(JsonValueKind.Null, reloaded[1].GetProperty("feedback").ValueKind); // unchecked items carry no answers
        await using var db = database.CreateContext();
        var attemptId = long.Parse(player.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        var item = await db.AttemptItems.Include(i => i.SelectedOptions).SingleAsync(i => i.AttemptId == attemptId && i.Position == 1);
        Assert.NotNull(item.CheckedAt);
        Assert.Equal(wrong, item.SelectedOptions.Single().QuestionOptionId);
    }

    [Fact]
    public async Task Using_a_hint_is_recorded()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var player = await StartAsync(learner, await SeedAsync(ShowAnswers.AfterEach, hints: true), "Practice");
        var (token, items) = await StateAsync(learner, player);

        using var hint = await HelpAsync(learner, player, token, "Hint", 2);

        Assert.True(items[1].GetProperty("hasHint").GetBoolean());
        Assert.Equal("Petunjuk 2", JsonDocument.Parse(await hint.Content.ReadAsStringAsync()).RootElement.GetProperty("hint").GetString());
        await using var db = database.CreateContext();
        var attemptId = long.Parse(player.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True((await db.AttemptItems.SingleAsync(i => i.AttemptId == attemptId && i.Position == 2)).HintUsed);
    }

    [Theory]
    [InlineData("Simulation", ShowAnswers.AfterEach)]
    [InlineData("Practice", ShowAnswers.AfterSubmit)]
    public async Task Without_practice_and_answers_after_each_there_is_no_check(string mode, ShowAnswers showAnswers)
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var player = await StartAsync(learner, await SeedAsync(showAnswers, hints: true), mode);
        var (token, items) = await StateAsync(learner, player);
        await SaveAsync(learner, player, token, 1, Option(items[0], 0), HttpStatusCode.NoContent);

        using var check = await HelpAsync(learner, player, token, "Check", 1);

        Assert.Equal(HttpStatusCode.Forbidden, check.StatusCode);
        if (mode == "Simulation")
        {
            using var hint = await HelpAsync(learner, player, token, "Hint", 1);
            Assert.Equal(HttpStatusCode.Forbidden, hint.StatusCode);
            Assert.False(items[0].GetProperty("hasHint").GetBoolean());
        }
    }

    [Fact]
    public async Task An_unanswered_item_cannot_be_checked()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var player = await StartAsync(learner, await SeedAsync(ShowAnswers.AfterEach, hints: true), "Practice");
        var (token, _) = await StateAsync(learner, player);

        using var check = await HelpAsync(learner, player, token, "Check", 1);

        Assert.Equal(HttpStatusCode.Forbidden, check.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<long> SeedAsync(ShowAnswers showAnswers, bool hints)
    {
        var preset = new RulePreset { Name = "Bantuan " + TestData.Unique(), ShowAnswers = showAnswers, HintsEnabled = hints, ShuffleQuestions = false, ShuffleOptions = false };
        var exam = TestData.Exam().Free().WithRules(preset).WithQuestions(2).Build();
        foreach (var eq in exam.Questions)
        {
            eq.Question!.Hint = $"Petunjuk {eq.SortOrder}";
        }

        await using var db = database.CreateContext();
        return (await db.SaveAsync(exam)).Id;
    }

    private static long Option(JsonElement item, int index) => item.GetProperty("options")[index].GetProperty("id").GetInt64();

    private static async Task<string> StartAsync(HttpClient client, long examId, string mode)
    {
        using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", new Dictionary<string, string>
        {
            ["Form.Mode"] = mode,
            ["Form.Untimed"] = "true",
            ["Form.HintsEnabled"] = "true",
            ["Form.ShuffleQuestions"] = "false",
            ["Form.ShuffleOptions"] = "false",
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

    private static async Task SaveAsync(HttpClient client, string player, string token, int position, long option, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{player}/jawab", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["position"] = position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["selected"] = option.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["flagged"] = "false",
                ["seconds"] = "3",
            }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> HelpAsync(HttpClient client, string player, string token, string handler, int position)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{player}/bantuan?handler={handler}", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["position"] = position.ToString(System.Globalization.CultureInfo.InvariantCulture) }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }

    [GeneratedRegex("<script type=\"application/json\" id=\"quiz-state\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex StatePattern();
}
