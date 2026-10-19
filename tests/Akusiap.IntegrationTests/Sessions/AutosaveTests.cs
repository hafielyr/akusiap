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

/// <summary>SES-04: answers and flags are saved one by one, restored on reopening, and refused after the deadline.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class AutosaveTests(PostgresFixture database)
{
    [Fact]
    public async Task Saves_are_idempotent_and_reopening_restores_answers_and_flags()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Free().WithQuestions(3));
        var player = await StartAsync(learner, examId);
        var (token, items) = await StateAsync(learner, player);
        var options = items[0].GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetInt64()).ToList();

        using var first = await SaveAsync(learner, player, token, 1, [options[0]], flagged: false, seconds: 12);
        using var again = await SaveAsync(learner, player, token, 1, [options[0]], flagged: false, seconds: 3);
        using var changed = await SaveAsync(learner, player, token, 1, [options[1]], flagged: true, seconds: 5);
        var (_, reopened) = await StateAsync(learner, player);

        Assert.Equal((HttpStatusCode.NoContent, HttpStatusCode.NoContent, HttpStatusCode.NoContent), (first.StatusCode, again.StatusCode, changed.StatusCode));
        Assert.Equal([options[1]], reopened[0].GetProperty("selected").EnumerateArray().Select(s => s.GetInt64()));
        Assert.True(reopened[0].GetProperty("flagged").GetBoolean());
        await using var db = database.CreateContext();
        var attemptId = AttemptId(player);
        var item = await db.AttemptItems.Include(i => i.SelectedOptions).SingleAsync(i => i.AttemptId == attemptId && i.Position == 1);
        Assert.Equal((true, true, 20), (item.IsAnswered, item.IsFlagged, item.TimeSpentSeconds));
        Assert.Single(item.SelectedOptions);
        Assert.NotNull(item.AnsweredAt);
        Assert.Equal(20, (await db.Attempts.SingleAsync(a => a.Id == attemptId)).TimeSpentSeconds);
    }

    [Fact]
    public async Task Wrong_options_other_viewers_and_late_saves_are_refused()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Batas " + TestData.Unique(), TimeLimitMinutes = 10 };
        var examId = await SeedAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(2));
        var player = await StartAsync(learner, examId, timed: true);
        var (token, items) = await StateAsync(learner, player);
        var options = items[0].GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetInt64()).ToList();
        var otherQuestionOption = items[1].GetProperty("options")[0].GetProperty("id").GetInt64();
        var (otherToken, _) = await StateAsync(other, "/ujian/" + examId);

        using var twoForSingle = await SaveAsync(learner, player, token, 1, [options[0], options[1]], false, 1);
        using var foreignOption = await SaveAsync(learner, player, token, 1, [otherQuestionOption], false, 1);
        using var notMine = await SaveAsync(other, player, otherToken, 1, [options[0]], false, 1);
        app.Clock.Advance(TimeSpan.FromMinutes(11));
        using var late = await SaveAsync(learner, player, token, 1, [options[0]], false, 1);

        Assert.Equal(HttpStatusCode.BadRequest, twoForSingle.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, foreignOption.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, notMine.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);
    }

    [Fact]
    public async Task The_exam_page_offers_to_continue_an_open_session()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Free().WithQuestions(2));
        var before = await learner.GetStringAsync(new Uri($"/ujian/{examId}", UriKind.Relative));
        var player = await StartAsync(learner, examId);

        var after = await learner.GetStringAsync(new Uri($"/ujian/{examId}", UriKind.Relative));

        Assert.DoesNotContain("data-testid=\"resume-banner\"", before, StringComparison.Ordinal);
        Assert.Contains($"href=\"{player}\"", after, StringComparison.Ordinal);
    }

    // ---------- Helpers ----------

    private async Task<long> SeedAsync(Exam exam)
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(exam)).Id;
    }

    private static long AttemptId(string player) => long.Parse(player.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);

    private static async Task<string> StartAsync(HttpClient client, long examId, bool timed = false)
    {
        var form = new Dictionary<string, string>
        {
            ["Form.Mode"] = "Practice",
            ["Form.Untimed"] = timed ? "false" : "true",
            ["Form.ShuffleQuestions"] = "false",
            ["Form.ShuffleOptions"] = "false",
        };
        if (timed)
        {
            form["Form.TimeLimitMinutes"] = "10";
        }

        using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }

    /// <summary>The antiforgery token and items from the player's embedded state (or just a token from any page).</summary>
    private static async Task<(string Token, List<JsonElement> Items)> StateAsync(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(new Uri(url, UriKind.Relative));
        var match = StatePattern().Match(html);
        if (!match.Success)
        {
            return (await client.GetAntiforgeryTokenAsync("/learn/profil"), []);
        }

        var state = JsonDocument.Parse(match.Groups[1].Value).RootElement;
        return (state.GetProperty("token").GetString()!, [.. state.GetProperty("items").EnumerateArray()]);
    }

    private static async Task<HttpResponseMessage> SaveAsync(HttpClient client, string player, string token, int position, long[] selected, bool flagged, int seconds)
    {
        var fields = new List<KeyValuePair<string, string>>
        {
            new("position", position.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new("flagged", flagged ? "true" : "false"),
            new("seconds", seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        };
        fields.AddRange(selected.Select(id => new KeyValuePair<string, string>("selected", id.ToString(System.Globalization.CultureInfo.InvariantCulture))));
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{player}/jawab", UriKind.Relative)) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }

    [GeneratedRegex("<script type=\"application/json\" id=\"quiz-state\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex StatePattern();
}
