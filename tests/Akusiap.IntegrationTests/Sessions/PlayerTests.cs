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
        Assert.DoesNotContain("isCorrect", html, StringComparison.OrdinalIgnoreCase);
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
}
