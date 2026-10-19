using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Jobs;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Sessions;

/// <summary>SES-06: submitting scores the attempt once; deadlines close it on access or by the sweep.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class SubmitTests(PostgresFixture database)
{
    [Fact]
    public async Task Submitting_scores_unanswered_as_wrong_and_a_second_submit_changes_nothing()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Lulus " + TestData.Unique(), PassMarkPercent = 30, ShuffleOptions = false, ShuffleQuestions = false };
        var examId = await SeedAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(3));
        var player = await StartAsync(learner, examId);
        var (token, items) = await StateAsync(learner, player);
        await SaveAsync(learner, player, token, 1, Option(items[0], 0)); // the first option is the correct one
        await SaveAsync(learner, player, token, 2, Option(items[1], 1));

        using var submitted = await SubmitAsync(learner, player);
        app.Clock.Advance(TimeSpan.FromMinutes(5));
        using var again = await SubmitAsync(learner, player);
        var page = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri(player, UriKind.Relative)));

        Assert.Equal((HttpStatusCode.Redirect, HttpStatusCode.Redirect), (submitted.StatusCode, again.StatusCode));
        var attempt = await LoadAsync(player);
        Assert.Equal((AttemptStatus.Submitted, 1, 3, 33.33m, (bool?)true), (attempt.Status, attempt.CorrectCount, attempt.TotalCount, attempt.ScorePercent!.Value, attempt.Passed));
        Assert.Equal(app.Clock.UtcNow.AddMinutes(-5), attempt.SubmittedAt);
        Assert.Equal([true, false, false], attempt.Items.OrderBy(i => i.Position).Select(i => i.IsCorrect));
        Assert.Contains("1 dari 3 benar", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_an_attempt_past_its_deadline_submits_it_at_the_deadline()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Waktu " + TestData.Unique(), TimeLimitMinutes = 10, ShuffleOptions = false };
        var examId = await SeedAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(2));
        var player = await StartAsync(learner, examId, minutes: 10);
        var (token, items) = await StateAsync(learner, player);
        await SaveAsync(learner, player, token, 1, Option(items[0], 0));
        var startedAt = app.Clock.UtcNow;
        app.Clock.Advance(TimeSpan.FromMinutes(12));

        var page = await learner.GetStringAsync(new Uri(player, UriKind.Relative));

        Assert.Contains("data-testid=\"session-ended\"", page, StringComparison.Ordinal);
        var attempt = await LoadAsync(player);
        Assert.Equal((AttemptStatus.Submitted, startedAt.AddMinutes(10), 600, 1), (attempt.Status, attempt.SubmittedAt!.Value, attempt.TimeSpentSeconds, attempt.CorrectCount));
    }

    [Fact]
    public async Task The_sweep_expires_overdue_attempts_and_scores_them()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Sapu " + TestData.Unique(), TimeLimitMinutes = 5 };
        var timedExam = await SeedAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(2));
        var untimedExam = await SeedAsync(TestData.Exam().Free().WithQuestions(2));
        var timed = await StartAsync(learner, timedExam, minutes: 5);
        var untimed = await StartAsync(learner, untimedExam);
        app.Clock.Advance(TimeSpan.FromMinutes(6));

        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<SweepExpiredAttemptsJob>().RunAsync(CancellationToken.None);
        }

        var expired = await LoadAsync(timed);
        Assert.Equal((AttemptStatus.Expired, 0, 2, 0m), (expired.Status, expired.CorrectCount, expired.TotalCount, expired.ScorePercent!.Value));
        Assert.Equal(AttemptStatus.InProgress, (await LoadAsync(untimed)).Status);
    }

    [Fact]
    public async Task Other_users_cannot_submit()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var player = await StartAsync(learner, await SeedAsync(TestData.Exam().Free().WithQuestions(1)));

        using var response = await other.PostFormAsync("/learn/profil", new Dictionary<string, string>(), $"{player}?handler=Submit");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(AttemptStatus.InProgress, (await LoadAsync(player)).Status);
    }

    // ---------- Helpers ----------

    private async Task<long> SeedAsync(Exam exam)
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(exam)).Id;
    }

    private async Task<Attempt> LoadAsync(string player)
    {
        var id = long.Parse(player.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        await using var db = database.CreateContext();
        return await db.Attempts.Include(a => a.Items).SingleAsync(a => a.Id == id);
    }

    private static long Option(JsonElement item, int index) => item.GetProperty("options")[index].GetProperty("id").GetInt64();

    private static async Task<string> StartAsync(HttpClient client, long examId, int? minutes = null)
    {
        var form = new Dictionary<string, string>
        {
            ["Form.Mode"] = "Practice",
            ["Form.Untimed"] = minutes is null ? "true" : "false",
            ["Form.ShuffleQuestions"] = "false",
            ["Form.ShuffleOptions"] = "false",
        };
        if (minutes is { } m)
        {
            form["Form.TimeLimitMinutes"] = m.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", form);
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
                ["position"] = position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["selected"] = option.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["flagged"] = "false",
                ["seconds"] = "5",
            }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, string player) =>
        client.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");

    [GeneratedRegex("<script type=\"application/json\" id=\"quiz-state\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex StatePattern();
}
