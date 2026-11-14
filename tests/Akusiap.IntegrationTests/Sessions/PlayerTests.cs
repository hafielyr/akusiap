using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Sessions;

/// <summary>SES-03: only the attempt's owner (or its guest browser) gets the player, which never carries the answers.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class PlayerTests(PostgresFixture database)
{
    [Fact]
    public async Task The_owner_gets_the_questions_in_the_stored_order_without_the_answers()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(TestData.Exam().Free().WithQuestions(3));
        var player = await StartAsync(learner, examId);

        var html = await learner.GetStringAsync(new Uri(player, UriKind.Relative));
        using var state = State(html);

        var items = state.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal([1, 2, 3], items.Select(i => i.GetProperty("position").GetInt32()));
        // The embedded state carries no answers until an item is checked (SES-05).
        var stateJson = StatePattern().Match(html).Groups[1].Value;
        Assert.DoesNotContain("correctOptionIds", stateJson, StringComparison.Ordinal);
        Assert.DoesNotContain("isCorrect", stateJson, StringComparison.OrdinalIgnoreCase);
        await using var db = database.CreateContext();
        var attemptId = long.Parse(player.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        var first = await db.AttemptItems.SingleAsync(i => i.AttemptId == attemptId && i.Position == 1);
        Assert.Equal(
            JsonSerializer.Deserialize<List<long>>(first.OptionOrder!),
            items[0].GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetInt64()));
        Assert.Equal(JsonValueKind.Null, state.RootElement.GetProperty("secondsLeft").ValueKind);
    }

    [Fact]
    public async Task Other_users_and_other_browsers_get_404()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var owner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var stranger = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var free = await SeedAsync(TestData.Exam().Free().WithQuestions(2));
        var paid = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(3, trial: 3));
        var ownerPlayer = await StartAsync(owner, free);
        var guestPlayer = await StartAsync(guest, paid);

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(new Uri(ownerPlayer, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri(ownerPlayer, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync(new Uri(guestPlayer, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(new Uri(guestPlayer, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await guest.GetAsync(new Uri(guestPlayer, UriKind.Relative))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(new Uri("/sesi/987654321", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task Timed_sessions_count_down_from_the_server_deadline()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Waktu " + TestData.Unique(), TimeLimitMinutes = 30 };
        var examId = await SeedAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(2));
        var player = await StartAsync(learner, examId, untimed: false);
        app.Clock.Advance(TimeSpan.FromMinutes(10));

        var html = await learner.GetStringAsync(new Uri(player, UriKind.Relative));
        using var state = State(html);

        Assert.Equal(20 * 60, state.RootElement.GetProperty("secondsLeft").GetInt32());
        Assert.Contains("data-testid=\"timer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_question_carries_its_section_and_topic_in_the_player_and_the_review()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Label " + TestData.Unique(), ShuffleQuestions = false };
        var exam = TestData.Exam().Free().WithRules(preset).WithQuestions(3).Build();
        exam.Questions.First().Question!.Topic!.Name = "Tes Wawasan Kebangsaan (TWK)";
        var numeric = new Topic { Subject = exam.Subject, Name = "TIU · Numerik" };
        var plain = new Topic { Subject = exam.Subject, Name = "Bahasa Indonesia" };
        exam.Questions.Single(q => q.SortOrder == 2).Question!.Topic = numeric;
        exam.Questions.Single(q => q.SortOrder == 3).Question!.Topic = plain;
        var player = await StartAsync(learner, await SeedAsync(exam));

        var html = await learner.GetStringAsync(new Uri(player, UriKind.Relative));
        using var state = State(html);
        var labels = state.RootElement.GetProperty("items").EnumerateArray()
            .Select(i => (i.GetProperty("section").GetString(), i.GetProperty("topic").GetString())).ToList();
        using var submitted = await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");
        var review = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"{player}/pembahasan", UriKind.Relative)));

        Assert.Equal([("TWK", "Tes Wawasan Kebangsaan"), ("TIU", "Numerik"), (null, "Bahasa Indonesia")], labels);
        Assert.Contains("data-testid=\"question-label\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        Assert.Equal(3, ReviewLabelPattern().Count(review));
        Assert.Contains("<span class=\"sr-only\">Bagian:</span> TWK</span>", review, StringComparison.Ordinal);
        Assert.Contains("<span class=\"sr-only\">Topik:</span> Numerik</span>", review, StringComparison.Ordinal);
    }

    // ---------- Helpers ----------

    private async Task<long> SeedAsync(Exam exam)
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(exam)).Id;
    }

    private static async Task<string> StartAsync(HttpClient client, long examId, bool untimed = true)
    {
        var form = new Dictionary<string, string>
        {
            ["Form.Mode"] = "Practice",
            ["Form.Untimed"] = untimed ? "true" : "false",
            ["Form.HintsEnabled"] = "false",
            ["Form.ShuffleQuestions"] = "false",
            ["Form.ShuffleOptions"] = "true",
        };
        if (!untimed)
        {
            form["Form.TimeLimitMinutes"] = "30";
        }

        using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }

    private static JsonDocument State(string html) =>
        JsonDocument.Parse(StatePattern().Match(html).Groups[1].Value);

    [GeneratedRegex("<script type=\"application/json\" id=\"quiz-state\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex StatePattern();

    [GeneratedRegex("data-testid=\"question-label\"")]
    private static partial Regex ReviewLabelPattern();
}
