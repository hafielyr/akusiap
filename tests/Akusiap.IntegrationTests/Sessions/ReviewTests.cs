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

/// <summary>SES-08: the answer review keeps the attempt's option order, filters wrong/flagged and survives retired questions.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class ReviewTests(PostgresFixture database)
{
    [Fact]
    public async Task The_review_uses_the_stored_option_order_and_retired_questions_still_render()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Bahas " + TestData.Unique(), ShuffleQuestions = false, ShuffleOptions = true };
        var exam = TestData.Exam().Free().WithRules(preset).WithQuestions(3).Build();
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(exam)).Id;
        }

        var client = new FlowClient(learner);
        var player = await client.StartAsync(examId, shuffleOptions: true);
        var (token, items) = await client.StateAsync(player);
        var correctFirst = Correct(items[0]);
        await client.SaveAsync(player, token, 1, correctFirst, flagged: false);
        var wrongSecond = Wrong(items[1]);
        await client.SaveAsync(player, token, 2, wrongSecond, flagged: true);
        using (await client.SubmitAsync(player))
        {
        }

        await using (var db = database.CreateContext())
        {
            var attemptId = long.Parse(player[(player.LastIndexOf('/') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
            var questionId = await db.AttemptItems.Where(i => i.AttemptId == attemptId && i.Position == 1).Select(i => i.QuestionId).SingleAsync();
            await db.Questions.Where(q => q.Id == questionId).ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, QuestionStatus.Retired));
        }

        var html = await learner.GetStringAsync(new Uri($"{player}/pembahasan", UriKind.Relative));

        Assert.Equal(3, ItemPattern().Count(html));
        for (var i = 0; i < items.Count; i++)
        {
            var shown = OptionIds(Block(html, i + 1));
            var stored = items[i].GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetInt64()).ToList();
            Assert.Equal(stored, shown);
        }

        Assert.Contains($"data-option=\"{correctFirst}\" data-selected=\"true\" data-is-correct=\"true\"", Block(html, 1), StringComparison.Ordinal);
        Assert.Contains($"data-option=\"{wrongSecond}\" data-selected=\"true\" data-is-correct=\"false\"", Block(html, 2), StringComparison.Ordinal);
        Assert.Contains("Pembahasan soal nomor 1.", html, StringComparison.Ordinal);

        var wrong = await learner.GetStringAsync(new Uri($"{player}/pembahasan?tampil=salah", UriKind.Relative));
        var flagged = await learner.GetStringAsync(new Uri($"{player}/pembahasan?tampil=ditandai", UriKind.Relative));
        Assert.Equal(["2", "3"], ItemPattern().Matches(wrong).Select(m => m.Groups[1].Value));
        Assert.Equal(["2"], ItemPattern().Matches(flagged).Select(m => m.Groups[1].Value));
    }

    [Fact]
    public async Task Never_hides_answers_and_strangers_and_open_attempts_get_404()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Rahasia " + TestData.Unique(), ShowAnswers = ShowAnswers.Never, ShuffleOptions = false, ShuffleQuestions = false };
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(2).Build())).Id;
        }

        var client = new FlowClient(learner);
        var player = await client.StartAsync(examId, shuffleOptions: false);
        using var open = await learner.GetAsync(new Uri($"{player}/pembahasan", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, open.StatusCode);

        using (await client.SubmitAsync(player))
        {
        }

        var html = await learner.GetStringAsync(new Uri($"{player}/pembahasan", UriKind.Relative));
        using var stranger = await other.GetAsync(new Uri($"{player}/pembahasan", UriKind.Relative));

        Assert.Contains("data-testid=\"review-hidden\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"review-item\"", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
    }

    private static long Correct(JsonElement item) => item.GetProperty("options").EnumerateArray().First(o => o.GetProperty("text").GetString() == "Pilihan 1").GetProperty("id").GetInt64();

    private static long Wrong(JsonElement item) => item.GetProperty("options").EnumerateArray().First(o => o.GetProperty("text").GetString() != "Pilihan 1").GetProperty("id").GetInt64();

    private static string Block(string html, int position)
    {
        var start = html.IndexOf($"data-position=\"{position}\"", StringComparison.Ordinal);
        var end = html.IndexOf("data-testid=\"review-item\"", start, StringComparison.Ordinal);
        return end < 0 ? html[start..] : html[start..end];
    }

    private static List<long> OptionIds(string block) =>
        [.. OptionPattern().Matches(block).Select(m => long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))];

    [GeneratedRegex("data-testid=\"review-item\" data-position=\"(\\d+)\"")]
    private static partial Regex ItemPattern();

    [GeneratedRegex("data-testid=\"review-option\" data-option=\"(\\d+)\"")]
    private static partial Regex OptionPattern();

    private sealed partial class FlowClient(HttpClient client)
    {
        public async Task<string> StartAsync(long examId, bool shuffleOptions)
        {
            using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", new Dictionary<string, string>
            {
                ["Form.Mode"] = "Practice", ["Form.Untimed"] = "true", ["Form.ShuffleQuestions"] = "false", ["Form.ShuffleOptions"] = shuffleOptions ? "true" : "false",
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

        public async Task SaveAsync(string player, string token, int position, long option, bool flagged)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{player}/jawab", UriKind.Relative))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["position"] = position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["selected"] = option.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["flagged"] = flagged ? "true" : "false",
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
