using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Planning;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Planning;

/// <summary>ANL-01, ANL-02 (D-136, D-137): the analysis page and the plan on the goal page, from real attempts.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class LearningAnalysisTests(PostgresFixture database)
{
    [Fact]
    public async Task Topics_come_weakest_first_and_the_plan_reviews_cards_practises_the_weak_topic_and_simulates()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, weakTopic, strongTopic) = await TwoTopicExamAsync();

        // Topic A: 1 of 4 right; topic B: 4 of 4 right.
        await TakeAsync(learner, examId, rightPositions: [1, 5, 6, 7, 8]);
        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri("/learn/analisis", UriKind.Relative)));

        Assert.Contains($"data-exam=\"{examId}\"", html, StringComparison.Ordinal);
        Assert.Equal([(weakTopic, "Weak"), (strongTopic, "Strong")], Topics(html));
        Assert.Contains("Paling lemah: Topik A", html, StringComparison.Ordinal);
        Assert.Contains("1 dari 4 soal yang dicoba kini benar", html, StringComparison.Ordinal);
        Assert.Equal(["ReviewCards", "PractiseTopic", "Simulation", "SetGoal"], StepKinds(html));
        Assert.Contains("Ulas 3 kartu hafalan", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ujian/{examId}/mulai?topik={weakTopic}&jumlah=4\"", html, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ujian/{examId}/mulai?mode=Simulation\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fixing_a_mistake_counts_at_once_because_the_latest_answer_wins()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, weakTopic, _) = await TwoTopicExamAsync();
        await TakeAsync(learner, examId, rightPositions: [1, 5, 6, 7, 8]);
        app.Clock.Advance(TimeSpan.FromMinutes(5));

        await TakeAsync(learner, examId, rightPositions: [1, 2, 3, 4, 5, 6, 7, 8]);
        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri("/learn/analisis", UriKind.Relative)));

        Assert.DoesNotContain(Topics(html), t => t.Strength != "Strong");
        Assert.DoesNotContain("data-testid=\"analysis-weakest-topic\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-topic=\"{weakTopic}\" data-strength=\"Strong\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_learner_without_tests_sees_the_empty_state_and_learn_links_to_the_page()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        var html = await learner.GetStringAsync(new Uri("/learn/analisis", UriKind.Relative));
        var learn = await learner.GetStringAsync(new Uri("/learn", UriKind.Relative));

        Assert.Contains("data-testid=\"analysis-empty\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/learn/analisis\"", learn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_goal_page_shows_the_plan_without_a_set_goal_step()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var (examId, _, _) = await TwoTopicExamAsync();
        await TakeAsync(learner, examId, rightPositions: [1, 5, 6, 7, 8]);
        long goalId;
        await using (var db = database.CreateContext())
        {
            goalId = (await db.SaveAsync(new StudyGoal
            {
                UserId = user.Id, ExamId = examId, ExamDate = DateOnly.FromDateTime(TestData.Now).AddDays(60), CreatedAt = TestData.Now, UpdatedAt = TestData.Now,
            })).Id;
        }

        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"/learn/target/{goalId}", UriKind.Relative)));
        var analysis = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri("/learn/analisis", UriKind.Relative)));

        Assert.Contains("data-testid=\"goal-analysis\"", html, StringComparison.Ordinal);
        Assert.Equal(["ReviewCards", "PractiseTopic", "Simulation"], StepKinds(html));
        Assert.Contains($"href=\"/learn/target/{goalId}\"", analysis, StringComparison.Ordinal);
    }

    /// <summary>A free exam (full access for registered users) with topic A (questions 1–4) and topic B (5–8), in order.</summary>
    private async Task<(long ExamId, long TopicA, long TopicB)> TwoTopicExamAsync()
    {
        var preset = new RulePreset { Name = "Analisis " + TestData.Unique(), ShuffleQuestions = false, ShuffleOptions = false };
        var exam = TestData.Exam().Free().WithRules(preset).WithQuestions(8).Build();
        var first = exam.Questions.First().Question!.Topic!;
        first.Name = "Topik A " + TestData.Unique();
        var other = new Topic { Subject = exam.Subject, Name = "Topik B " + TestData.Unique() };
        exam.Questions.Where(q => q.SortOrder > 4).ToList().ForEach(q => q.Question!.Topic = other);
        await using var db = database.CreateContext();
        await db.SaveAsync(exam);
        return (exam.Id, first.Id, other.Id);
    }

    /// <summary>Takes all 8 questions in Practice: the listed positions right, the rest wrong; then submits.</summary>
    private static async Task TakeAsync(HttpClient client, long examId, int[] rightPositions)
    {
        using var started = await client.PostFormAsync($"/ujian/{examId}/mulai", new Dictionary<string, string>
        {
            ["Form.Mode"] = "Practice", ["Form.QuestionCount"] = "8", ["Form.Untimed"] = "true", ["Form.ShuffleQuestions"] = "false", ["Form.ShuffleOptions"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        var player = started.Headers.Location!.OriginalString;
        var html = await client.GetStringAsync(new Uri(player, UriKind.Relative));
        var state = JsonDocument.Parse(StatePattern().Match(html).Groups[1].Value).RootElement;
        var token = state.GetProperty("token").GetString()!;
        var items = state.GetProperty("items").EnumerateArray().ToList();
        for (var position = 1; position <= items.Count; position++)
        {
            var option = items[position - 1].GetProperty("options")[rightPositions.Contains(position) ? 0 : 1].GetProperty("id").GetInt64();
            using var save = new HttpRequestMessage(HttpMethod.Post, new Uri($"{player}/jawab", UriKind.Relative))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["position"] = position.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["selected"] = option.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["flagged"] = "false",
                    ["seconds"] = "4",
                }),
            };
            save.Headers.Add("RequestVerificationToken", token);
            using var saved = await client.SendAsync(save);
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }

        using var submitted = await client.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
    }

    private static List<(long Topic, string Strength)> Topics(string html) =>
        [.. TopicPattern().Matches(html).Select(m => (long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups[2].Value))];

    private static List<string> StepKinds(string html) => [.. StepPattern().Matches(html).Select(m => m.Groups[1].Value)];

    [GeneratedRegex("<script type=\"application/json\" id=\"quiz-state\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex StatePattern();

    [GeneratedRegex("data-testid=\"analysis-topic\" data-topic=\"(\\d+)\" data-strength=\"(\\w+)\"")]
    private static partial Regex TopicPattern();

    [GeneratedRegex("data-testid=\"analysis-step\" data-kind=\"(\\w+)\"")]
    private static partial Regex StepPattern();
}
