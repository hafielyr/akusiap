using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Questions;

/// <summary>DIF-01 (D-140): finished sessions fill each question's right, wrong and marked counts, which set its label.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class QuestionStatsTests(PostgresFixture database)
{
    [Fact]
    public async Task A_finished_session_counts_right_wrong_and_marked_even_when_unmarked_later()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, questionIds) = await ExamAsync();
        var player = await StartAsync(learner, examId);
        var (token, items) = await StateAsync(learner, player);

        await SaveAsync(learner, player, token, 1, Option(items[0], 0), flagged: false); // right
        await SaveAsync(learner, player, token, 2, Option(items[1], 1), flagged: true); // wrong, marked
        await SaveAsync(learner, player, token, 3, Option(items[2], 0), flagged: true); // marked ...
        await SaveAsync(learner, player, token, 3, Option(items[2], 0), flagged: false); // ... then sure: still counts as marked
        // 4 stays blank: no answer to count.
        await using (var db = database.CreateContext())
        {
            Assert.False(await db.QuestionStats.AnyAsync(s => questionIds.Contains(s.QuestionId)), "Nothing counts before the session ends");
        }

        using var submitted = await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");

        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
        await using var check = database.CreateContext();
        var stats = await check.QuestionStats.Where(s => questionIds.Contains(s.QuestionId)).ToDictionaryAsync(s => s.QuestionId);
        Assert.Equal((1, 0, 0), Counts(stats[questionIds[0]]));
        Assert.Equal((0, 1, 1), Counts(stats[questionIds[1]]));
        Assert.Equal((1, 0, 1), Counts(stats[questionIds[2]]));
        Assert.False(stats.ContainsKey(questionIds[3]));
    }

    [Fact]
    public async Task Ten_answers_label_the_question_in_the_player_and_the_review()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, questionIds) = await ExamAsync();
        await using (var db = database.CreateContext())
        {
            db.QuestionStats.AddRange(
                new QuestionStats { QuestionId = questionIds[0], CorrectCount = 9, WrongCount = 0, UpdatedAt = TestData.Now }, // 10th answer comes now
                new QuestionStats { QuestionId = questionIds[1], CorrectCount = 2, WrongCount = 8, UpdatedAt = TestData.Now });
            await db.SaveChangesAsync();
        }

        var player = await StartAsync(learner, examId);
        var (token, items) = await StateAsync(learner, player);
        var before = items.Select(i => i.GetProperty("difficulty").ValueKind == JsonValueKind.Null ? null : i.GetProperty("difficulty").GetString()).ToList();
        await SaveAsync(learner, player, token, 1, Option(items[0], 0), flagged: false);
        using (await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit"))
        {
        }

        var review = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"{player}/pembahasan", UriKind.Relative)));

        Assert.Equal([null, "Hard", null, null], before);
        Assert.Equal(["Easy", "Hard"], DifficultyPattern().Matches(review).Select(m => m.Groups[1].Value));
        Assert.Contains("Tingkat kesulitan:</span> Mudah", review, StringComparison.Ordinal);
        Assert.Contains("Tingkat kesulitan:</span> Sulit", review, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_admin_question_list_shows_the_measured_level_and_answer_count()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var exam = TestData.Exam().Free().WithQuestions(2).Build();
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(exam);
            db.QuestionStats.Add(new QuestionStats { QuestionId = exam.Questions.First().QuestionId, CorrectCount = 5, WrongCount = 7, MarkedCount = 1, UpdatedAt = TestData.Now });
            await db.SaveChangesAsync();
        }

        var html = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/soal?subjek={exam.SubjectId}", UriKind.Relative)));

        Assert.Equal(["Hard"], DifficultyPattern().Matches(html.Replace("measured-difficulty", "question-difficulty", StringComparison.Ordinal)).Select(m => m.Groups[1].Value));
        Assert.Contains("Sulit · 12 jawaban", html, StringComparison.Ordinal);
    }

    private static (int, int, int) Counts(QuestionStats s) => (s.CorrectCount, s.WrongCount, s.MarkedCount);

    private async Task<(long ExamId, List<long> QuestionIds)> ExamAsync()
    {
        var preset = new RulePreset { Name = "Statistik " + TestData.Unique(), ShuffleQuestions = false, ShuffleOptions = false };
        var exam = TestData.Exam().Free().WithRules(preset).WithQuestions(4).Build();
        await using var db = database.CreateContext();
        await db.SaveAsync(exam);
        return (exam.Id, [.. exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId)]);
    }

    private static long Option(JsonElement item, int index) => item.GetProperty("options")[index].GetProperty("id").GetInt64();

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

    private static async Task SaveAsync(HttpClient client, string player, string token, int position, long option, bool flagged)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"{player}/jawab", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["position"] = position.ToString(CultureInfo.InvariantCulture),
                ["selected"] = option.ToString(CultureInfo.InvariantCulture),
                ["flagged"] = flagged ? "true" : "false",
                ["seconds"] = "4",
            }),
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [GeneratedRegex("<script type=\"application/json\" id=\"quiz-state\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex StatePattern();

    [GeneratedRegex("data-testid=\"question-difficulty\" data-difficulty=\"(\\w+)\"")]
    private static partial Regex DifficultyPattern();
}
