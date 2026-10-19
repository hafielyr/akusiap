using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Akusiap.IntegrationTests.Sessions;

/// <summary>SES-07: the result page with the per-topic table and, for trials only, the buy/subscribe offer.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class ResultTests(PostgresFixture database)
{
    [Fact]
    public async Task The_topic_breakdown_adds_up_to_the_total_and_shows_pass_or_fail()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Hasil " + TestData.Unique(), PassMarkPercent = 50, ShuffleQuestions = false, ShuffleOptions = false };
        var exam = TestData.Exam().Free().WithRules(preset).WithQuestions(5).Build();
        var other = new Topic { Subject = exam.Subject, Name = "Topik B " + TestData.Unique() };
        exam.Questions.Where(q => q.SortOrder > 3).ToList().ForEach(q => q.Question!.Topic = other);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(exam)).Id;
        }

        var client = new FlowClient(learner);
        var player = await client.StartAsync(examId);
        var (token, items) = await client.StateAsync(player);
        await client.SaveAsync(player, token, 1, Option(items[0], 0)); // right
        await client.SaveAsync(player, token, 2, Option(items[1], 1)); // wrong
        await client.SaveAsync(player, token, 4, Option(items[3], 0)); // right (topic B)
        using var submitted = await client.SubmitAsync(player);

        Assert.Equal($"{player}/hasil", submitted.Headers.Location!.OriginalString);
        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(submitted.Headers.Location));
        var rows = TopicPattern().Matches(html).Select(m => (int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture))).ToList();
        Assert.Equal(5, rows.Sum(r => r.Item2));
        Assert.Equal(2, rows.Sum(r => r.Item1));
        Assert.Contains("2 dari 5 benar", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"result-passed\"", html, StringComparison.Ordinal);
        Assert.Contains("Belum lulus", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"trial-offer\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_trial_results_offer_to_buy_and_guests_go_through_sign_in()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(3, trial: 3).Build())).Id;
        }

        var client = new FlowClient(guest);
        var player = await client.StartAsync(examId);
        using (await client.SubmitAsync(player))
        {
        }

        var html = WebUtility.HtmlDecode(await guest.GetStringAsync(new Uri($"{player}/hasil", UriKind.Relative)));

        Assert.Contains("data-testid=\"trial-offer\"", html, StringComparison.Ordinal);
        Assert.Contains("Rp49.000", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/akun/masuk?ReturnUrl={Uri.EscapeDataString($"/checkout?exam={examId}")}\"", html.Replace("&amp;", "&", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_open_attempt_goes_back_to_the_player_and_strangers_get_404()
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

        var player = await new FlowClient(learner).StartAsync(examId);

        using var open = await learner.GetAsync(new Uri($"{player}/hasil", UriKind.Relative));
        using var stranger = await other.GetAsync(new Uri($"{player}/hasil", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        Assert.Equal(player, open.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
    }

    private static long Option(JsonElement item, int index) => item.GetProperty("options")[index].GetProperty("id").GetInt64();

    [GeneratedRegex("data-testid=\"topic-score\">(\\d+)/(\\d+)<")]
    private static partial Regex TopicPattern();

    /// <summary>Starts, answers and submits attempts through the pages, like the player does.</summary>
    private sealed partial class FlowClient(HttpClient client)
    {
        public async Task<string> StartAsync(long examId)
        {
            using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", new Dictionary<string, string>
            {
                ["Form.Mode"] = "Practice", ["Form.Untimed"] = "true", ["Form.ShuffleQuestions"] = "false", ["Form.ShuffleOptions"] = "false",
            });
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            return response.Headers.Location!.OriginalString;
        }

        public async Task<(string Token, List<JsonElement> Items)> StateAsync(string player)
        {
            var html = await client.GetStringAsync(new Uri(player, UriKind.Relative));
            var state = JsonDocument.Parse(StatePattern().Match(html).Groups[1].Value).RootElement;
            return (state.GetProperty("token").GetString()!, [.. state.GetProperty("items").EnumerateArray()]);
        }

        public async Task SaveAsync(string player, string token, int position, long option)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{player}/jawab", UriKind.Relative))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["position"] = position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["selected"] = option.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["flagged"] = "false",
                    ["seconds"] = "4",
                }),
            };
            request.Headers.Add("RequestVerificationToken", token);
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        public Task<HttpResponseMessage> SubmitAsync(string player) =>
            client.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");

        [GeneratedRegex("<script type=\"application/json\" id=\"quiz-state\">(.*?)</script>", RegexOptions.Singleline)]
        private static partial Regex StatePattern();
    }
}
