using System.Globalization;
using System.Net;
using Akusiap.Domain;
using Akusiap.Domain.Content;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Planning;

/// <summary>PLN-04 (D-122): a goal's progress page with answered and mastered shares per topic, weakest first.</summary>
[Collection(UsesDatabase.Name)]
public sealed class GoalProgressTests(PostgresFixture database)
{
    [Fact]
    public async Task Shares_follow_answers_and_cards_and_the_weakest_topic_comes_first()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long examId, topicA, topicB;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(4).Build());
            var q = exam.Questions.OrderBy(x => x.SortOrder).Select(x => x.Question!).ToList();
            topicA = q[0].TopicId;
            var other = new Topic { SubjectId = exam.SubjectId!.Value, Name = "Topik B " + TestData.Unique() };
            db.Topics.Add(other);
            await db.SaveChangesAsync();
            topicB = other.Id;
            (q[2].TopicId, q[3].TopicId) = (topicB, topicB);

            // Topic A: q0 right (mastered), q1 wrong with a fresh card (not mastered).
            // Topic B: q2 right but its card is still Learning (not mastered), q3 never answered.
            var attempt = new Attempt { UserId = user.Id, ExamId = exam.Id, Source = AttemptSource.Exam, Mode = AttemptMode.Practice, RulesSnapshot = "{}", StartedAt = app.Clock.UtcNow, LastSavedAt = app.Clock.UtcNow, Status = AttemptStatus.Submitted, SubmittedAt = app.Clock.UtcNow, TotalCount = 3 };
            foreach (var (question, position, correct) in new[] { (q[0], 1, true), (q[1], 2, false), (q[2], 3, true) })
            {
                attempt.Items.Add(new AttemptItem { QuestionId = question.Id, Position = position, IsAnswered = true, IsCorrect = correct, AnsweredAt = app.Clock.UtcNow });
            }

            db.Attempts.Add(attempt);
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = q[1].Id, State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow });
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = q[2].Id, State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddDays(1) });
            await db.SaveChangesAsync();
            examId = exam.Id;
        }

        await SetGoalAsync(learner, examId, JakartaTime.Today(app.Clock.UtcNow).AddDays(47));
        var goalId = await GoalIdAsync(user.Id, examId);
        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"/learn/target/{goalId}", UriKind.Relative)));

        Assert.Contains("data-testid=\"progress-answered\">75%", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"progress-mastered\">25%", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"progress-schedule\" data-behind=\"0\"", html, StringComparison.Ordinal);
        var b = html.IndexOf($"data-topic=\"{topicB}\"", StringComparison.Ordinal);
        var a = html.IndexOf($"data-topic=\"{topicA}\"", StringComparison.Ordinal);
        Assert.True(b >= 0 && a > b, "topic B (0% mastered) should come before topic A (50%)");
        Assert.Contains("Dikerjakan 50% · Dikuasai 0%", html[b..a], StringComparison.Ordinal);
        Assert.Contains("Dikerjakan 100% · Dikuasai 50%", html[a..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_learner_who_skips_days_is_behind_and_other_users_get_404()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        using var stranger = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(40).Build())).Id;
        }

        // 47 days: 40 learning days, one question a day on the straight line.
        await SetGoalAsync(learner, examId, JakartaTime.Today(app.Clock.UtcNow).AddDays(47));
        var goalId = await GoalIdAsync(user.Id, examId);
        app.Clock.Advance(TimeSpan.FromDays(10));
        var html = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"/learn/target/{goalId}", UriKind.Relative)));
        using var foreign = await stranger.GetAsync(new Uri($"/learn/target/{goalId}", UriKind.Relative));
        var today = await learner.GetStringAsync(new Uri("/learn/hari-ini", UriKind.Relative));
        var goals = await learner.GetStringAsync(new Uri("/learn/target", UriKind.Relative));

        Assert.Contains("data-behind=\"10\"", html, StringComparison.Ordinal);
        Assert.Contains("Tertinggal 10 soal dari jadwal", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Contains($"href=\"/learn/target/{goalId}\"", today, StringComparison.Ordinal);
        Assert.Contains($"href=\"/learn/target/{goalId}\"", goals, StringComparison.Ordinal);
    }

    private async Task<long> GoalIdAsync(Guid userId, long examId)
    {
        await using var db = database.CreateContext();
        return await db.StudyGoals.Where(g => g.UserId == userId && g.ExamId == examId && g.ArchivedAt == null).Select(g => g.Id).SingleAsync();
    }

    private static async Task SetGoalAsync(HttpClient client, long examId, DateOnly date)
    {
        using var response = await client.PostFormAsync("/learn/target", new Dictionary<string, string>
        {
            ["Input.ExamId"] = examId.ToString(CultureInfo.InvariantCulture),
            ["Input.ExamDate"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }
}
