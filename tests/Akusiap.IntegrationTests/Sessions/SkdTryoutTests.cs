using System.Globalization;
using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Sessions;

/// <summary>
/// CPNS-03, CPNS-04 and CPNS-05: an SKD exam's tryout picks 30 TWK, 35 TIU and 45 TKP in order, the result scores each
/// subtest against its passing grade (TKP by option points), and every result shows a short analysis.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed class SkdTryoutTests(PostgresFixture database)
{
    [Fact]
    public async Task A_tryout_takes_each_subtest_in_the_cat_order()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, topics) = await SeedSkdExamAsync();

        var player = await StartAsync(learner, examId, "Simulation");

        await using var db = database.CreateContext();
        var items = await db.AttemptItems.Where(i => i.AttemptId == IdOf(player)).OrderBy(i => i.Position)
            .Select(i => i.Question!.TopicId).ToListAsync();
        Assert.Equal(110, items.Count);
        Assert.All(items.Take(30), t => Assert.Equal(topics[SkdSubtest.Twk], t));
        Assert.All(items.Skip(30).Take(35), t => Assert.Equal(topics[SkdSubtest.Tiu], t));
        Assert.All(items.Skip(65), t => Assert.Equal(topics[SkdSubtest.Tkp], t));
    }

    [Fact]
    public async Task A_full_tryout_is_scored_per_subtest_with_tkp_points()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, topics) = await SeedSkdExamAsync();
        var player = await StartAsync(learner, examId, "Simulation");

        // 13 TWK right (65), 16 TIU right (80), every TKP answer worth 4 points (180): passes all three grades.
        await AnswerAsync(IdOf(player), topics, twkRight: 13, tiuRight: 16, tkpPoints: 4);
        using var submitted = await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");
        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(submitted.Headers.Location));

        Assert.Contains("data-testid=\"skd-score\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"skd-total\">325", html, StringComparison.Ordinal);
        Assert.Contains("Lolos ambang batas di ketiga subtes.", html, StringComparison.Ordinal);
        Assert.Contains("data-subtest=\"TKP\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"skd-subtest-score\">180<", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_subtest_below_its_grade_fails_and_says_how_far()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, topics) = await SeedSkdExamAsync();
        var player = await StartAsync(learner, examId, "Simulation");

        await AnswerAsync(IdOf(player), topics, twkRight: 12, tiuRight: 30, tkpPoints: 5);
        using var submitted = await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");
        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(submitted.Headers.Location));

        Assert.Contains("Belum lolos", html, StringComparison.Ordinal);
        Assert.Contains("Kurang 5 poin dari ambang batas.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Practice_on_an_skd_exam_shows_estimates_and_the_weakest_topic_to_practise()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, topics) = await SeedSkdExamAsync();
        var player = await StartAsync(learner, examId, "Practice", count: 40);

        await AnswerAsync(IdOf(player), topics, twkRight: 30, tiuRight: 0, tkpPoints: 5);
        app.Clock.Advance(TimeSpan.FromMinutes(40));
        using var submitted = await learner.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");
        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(submitted.Headers.Location));

        Assert.Contains("data-testid=\"skd-score\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"skd-total\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"analysis-weakest\"", html, StringComparison.Ordinal);
        Assert.Contains($"/ujian/{examId}/mulai?topik={topics[SkdSubtest.Tiu]}", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"analysis-pace\"", html, StringComparison.Ordinal);
        Assert.Contains("masuk flashcard", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_setup_page_preselects_a_topic_from_the_analysis_link()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, topics) = await SeedSkdExamAsync();

        var html = await learner.GetStringAsync(new Uri($"/ujian/{examId}/mulai?topik={topics[SkdSubtest.Tiu]}", UriKind.Relative));

        Assert.Matches($"value=\"{topics[SkdSubtest.Tiu]}\"\\s+checked=\"checked\"", html);
        Assert.Matches("<input[^>]*name=\"Form.QuestionCount\"[^>]*value=\"20\"", html);
    }

    // ---------- Helpers ----------

    /// <summary>A free SKD exam: 32 TWK, 37 TIU, 47 TKP (options scored 1–5) and 5 questions outside the SKD topics.</summary>
    private async Task<(long ExamId, Dictionary<SkdSubtest, long> Topics)> SeedSkdExamAsync()
    {
        var subject = new Subject { Name = "Seleksi " + TestData.Unique() };
        var topicFor = new Dictionary<SkdSubtest, Topic>
        {
            [SkdSubtest.Twk] = new() { Subject = subject, Name = "Wawasan (TWK) " + TestData.Unique() },
            [SkdSubtest.Tiu] = new() { Subject = subject, Name = "Intelegensia (TIU) " + TestData.Unique() },
            [SkdSubtest.Tkp] = new() { Subject = subject, Name = "Karakteristik (TKP) " + TestData.Unique() },
        };
        var other = new Topic { Subject = subject, Name = "Lain " + TestData.Unique() };
        var exam = new Exam
        {
            OwnerKind = OwnerKind.App,
            Subject = subject,
            RulePreset = new RulePreset { Name = "SKD " + TestData.Unique(), CatMode = true, TimeLimitMinutes = 100, QuestionCount = 110, ShuffleOptions = false },
            Title = "SKD " + TestData.Unique(),
            IsFree = true,
            Status = ExamStatus.Published,
            ScoringScheme = ScoringScheme.Skd,
            CreatedAt = TestData.Now,
        };
        var order = 0;
        foreach (var (topic, count, scored) in new[] { (topicFor[SkdSubtest.Twk], 32, false), (topicFor[SkdSubtest.Tiu], 37, false), (topicFor[SkdSubtest.Tkp], 47, true), (other, 5, false) })
        {
            for (var i = 0; i < count; i++)
            {
                var question = new Question
                {
                    Topic = topic,
                    QuestionType = QuestionType.SingleChoice,
                    Text = $"{topic.Name} soal {i}?",
                    Explanation = "Pembahasan.",
                    Status = QuestionStatus.Active,
                    CreatedAt = TestData.Now,
                    UpdatedAt = TestData.Now,
                };
                for (var o = 1; o <= 5; o++)
                {
                    question.Options.Add(new QuestionOption { Text = $"Pilihan {o}", IsCorrect = o == 5, SortOrder = o, Points = scored ? o : null });
                }

                exam.Questions.Add(new ExamQuestion { Question = question, SortOrder = ++order });
            }
        }

        await using var db = database.CreateContext();
        await db.SaveAsync(exam);
        return (exam.Id, topicFor.ToDictionary(t => t.Key, t => t.Value.Id));
    }

    /// <summary>Stores answers straight on the attempt: the first right ones per subtest, TKP options by their points.</summary>
    private async Task AnswerAsync(long attemptId, Dictionary<SkdSubtest, long> topics, int twkRight, int tiuRight, int tkpPoints)
    {
        await using var db = database.CreateContext();
        var items = await db.AttemptItems.Where(i => i.AttemptId == attemptId).OrderBy(i => i.Position)
            .Select(i => new { i.Id, i.Question!.TopicId, Options = i.Question.Options.Select(o => new { o.Id, o.IsCorrect, o.SortOrder }).ToList() })
            .ToListAsync();
        var right = new Dictionary<long, int> { [topics[SkdSubtest.Twk]] = twkRight, [topics[SkdSubtest.Tiu]] = tiuRight };
        foreach (var item in items)
        {
            long optionId;
            if (item.TopicId == topics[SkdSubtest.Tkp])
            {
                optionId = item.Options.Single(o => o.SortOrder == tkpPoints).Id;
            }
            else if (right.TryGetValue(item.TopicId, out var left) && left > 0)
            {
                right[item.TopicId] = left - 1;
                optionId = item.Options.Single(o => o.IsCorrect).Id;
            }
            else
            {
                optionId = item.Options.First(o => !o.IsCorrect).Id;
            }

            db.AttemptItemOptions.Add(new AttemptItemOption { AttemptItemId = item.Id, QuestionOptionId = optionId });
        }

        await db.SaveChangesAsync();
    }

    private static long IdOf(string player) => long.Parse(player.Split('/')[^1], CultureInfo.InvariantCulture);

    private static async Task<string> StartAsync(HttpClient client, long examId, string mode, int count = 110)
    {
        using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", new Dictionary<string, string>
        {
            ["Form.Mode"] = mode,
            ["Form.QuestionCount"] = count.ToString(CultureInfo.InvariantCulture),
            ["Form.Untimed"] = "true",
            ["Form.HintsEnabled"] = "true",
            ["Form.ShuffleQuestions"] = "false",
            ["Form.ShuffleOptions"] = "false",
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }
}
