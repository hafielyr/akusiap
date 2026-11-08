using System.Globalization;
using System.Net;
using Akusiap.Domain;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Planning;

/// <summary>PLN-02 (D-119, D-121): today's plan per goal on the Today page and the plan session it starts.</summary>
[Collection(UsesDatabase.Name)]
public sealed class StudyPlanTests(PostgresFixture database)
{
    private const string TodayUrl = "/learn/hari-ini";

    [Fact]
    public async Task The_quota_spreads_the_questions_and_a_missed_day_spreads_over_the_days_left()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SaveExamAsync(TestData.Exam().Free().WithQuestions(50));
        await SetGoalAsync(learner, examId, JakartaTime.Today(app.Clock.UtcNow).AddDays(12));

        // 12 days: 2 review days, 10 learning days, ceil(50 / 10) = 5.
        var first = await TodayAsync(learner);
        app.Clock.Advance(TimeSpan.FromDays(1));
        var afterMissedDay = await TodayAsync(learner);

        Assert.Equal("5", Testid(first, "plan-new"));
        Assert.Contains("0 dari 50 soal sudah dikerjakan · 10 hari belajar lagi", first, StringComparison.Ordinal);
        Assert.Equal("6", Testid(afterMissedDay, "plan-new")); // ceil(50 / 9)
        Assert.Contains("9 hari belajar lagi", afterMissedDay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_plan_session_takes_due_cards_first_then_new_questions_up_to_the_quota()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var exam = await SaveAsync(TestData.Exam().Free().WithQuestions(50));
        var questions = exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId).ToList();
        await using (var db = database.CreateContext())
        {
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = questions[40], State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        await SetGoalAsync(learner, exam.Id, JakartaTime.Today(app.Clock.UtcNow).AddDays(12));
        var before = await TodayAsync(learner);
        var player = await StartAsync(learner, await GoalIdAsync(user.Id, exam.Id));
        var again = await StartAsync(learner, await GoalIdAsync(user.Id, exam.Id));

        Assert.Equal(("5", "1"), (Testid(before, "plan-new"), Testid(before, "plan-cards")));
        Assert.Equal(player, again); // an unfinished session is continued, not replaced
        await using (var check = database.CreateContext())
        {
            var attempt = await check.Attempts.Include(a => a.Items).SingleAsync(a => a.Id == IdOf(player));
            Assert.Equal((AttemptSource.StudyPlan, AttemptMode.Practice, (long?)exam.Id), (attempt.Source, attempt.Mode, attempt.ExamId));
            Assert.Equal(6, attempt.Items.Count); // 1 due card + 5 new questions
            Assert.Equal(questions[40], attempt.Items.Single(i => i.Position == 1).QuestionId);
        }

        Assert.Contains("data-testid=\"plan-continue\"", await TodayAsync(learner), StringComparison.Ordinal);

        // Answering all six finishes today's tasks; the quota for the day stays the same.
        await AnswerAllAsync(IdOf(player), app.Clock.UtcNow);
        var done = await TodayAsync(learner);
        using var nothing = await learner.PostFormAsync(TodayUrl, Goal(await GoalIdAsync(user.Id, exam.Id)), $"{TodayUrl}?handler=Plan");

        Assert.Equal("0", Testid(done, "plan-new"));
        Assert.Contains("data-testid=\"plan-done\"", done, StringComparison.Ordinal);
        Assert.Contains("6 dari 50 soal sudah dikerjakan", done, StringComparison.Ordinal);
        Assert.Equal(TodayUrl, nothing.Headers.Location!.OriginalString);
        Assert.Contains("Tugas hari ini sudah selesai.", WebUtility.HtmlDecode(await TodayAsync(learner)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_due_card_never_also_comes_back_as_a_new_question_in_the_same_session()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var exam = await SaveAsync(TestData.Exam().Free().WithQuestions(3));
        var card = exam.Questions.OrderBy(q => q.SortOrder).First().QuestionId;
        await using (var db = database.CreateContext())
        {
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = card, State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        // Exam tomorrow: one learning day, so the quota asks for all 3 unanswered questions, the card's own among them.
        await SetGoalAsync(learner, exam.Id, JakartaTime.Today(app.Clock.UtcNow).AddDays(1));
        var player = await StartAsync(learner, await GoalIdAsync(user.Id, exam.Id));

        await using var check = database.CreateContext();
        var items = await check.AttemptItems.Where(i => i.AttemptId == IdOf(player)).OrderBy(i => i.Position).Select(i => i.QuestionId).ToListAsync();
        Assert.Equal(card, items[0]);
        Assert.Equal(exam.Questions.Select(q => q.QuestionId).Order(), items.Order());
    }

    [Fact]
    public async Task The_review_phase_offers_only_due_cards()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var exam = await SaveAsync(TestData.Exam().Free().WithQuestions(30));
        var card = exam.Questions.OrderBy(q => q.SortOrder).First().QuestionId;
        await SetGoalAsync(learner, exam.Id, JakartaTime.Today(app.Clock.UtcNow).AddDays(12));
        app.Clock.Advance(TimeSpan.FromDays(10)); // 2 days left: the review phase
        await using (var db = database.CreateContext())
        {
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = card, State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        var html = await TodayAsync(learner);
        var player = await StartAsync(learner, await GoalIdAsync(user.Id, exam.Id));

        Assert.Contains("data-phase=\"Review\"", html, StringComparison.Ordinal);
        Assert.Equal(("0", "1"), (Testid(html, "plan-new"), Testid(html, "plan-cards")));
        Assert.Contains("Fase ulasan: tidak ada soal baru.", html, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        var items = await check.AttemptItems.Where(i => i.AttemptId == IdOf(player)).Select(i => i.QuestionId).ToListAsync();
        Assert.Equal([card], items);
    }

    [Fact]
    public async Task Questions_outside_the_learners_access_never_appear()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var exam = await SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(8, trial: 3));
        var trial = exam.Questions.Where(q => q.IsTrial).Select(q => q.QuestionId).ToHashSet();
        var locked = exam.Questions.Where(q => !q.IsTrial).Select(q => q.QuestionId).First();
        await using (var db = database.CreateContext())
        {
            // A due card for a question the learner can't use yet is left out too.
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = locked, State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        await SetGoalAsync(learner, exam.Id, JakartaTime.Today(app.Clock.UtcNow).AddDays(1));
        var html = await TodayAsync(learner);
        var player = await StartAsync(learner, await GoalIdAsync(user.Id, exam.Id));

        Assert.Contains("0 dari 3 soal sudah dikerjakan", html, StringComparison.Ordinal);
        Assert.Equal(("3", "0"), (Testid(html, "plan-new"), Testid(html, "plan-cards")));
        await using var check = database.CreateContext();
        var items = await check.AttemptItems.Where(i => i.AttemptId == IdOf(player)).Select(i => i.QuestionId).ToListAsync();
        Assert.Equal(3, items.Count);
        Assert.All(items, id => Assert.Contains(id, trial));
    }

    [Fact]
    public async Task A_date_too_close_warns_and_another_users_goal_answers_404()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var owner = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(owner, RoleNames.Learner);
        using var stranger = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SaveExamAsync(TestData.Exam().Free().WithQuestions(90));
        await SetGoalAsync(learner, examId, JakartaTime.Today(app.Clock.UtcNow).AddDays(1));

        var html = WebUtility.HtmlDecode(await TodayAsync(learner));
        using var foreign = await stranger.PostFormAsync(TodayUrl, Goal(await GoalIdAsync(owner.Id, examId)), $"{TodayUrl}?handler=Plan");

        Assert.Equal("90", Testid(html, "plan-new"));
        Assert.Contains("90 soal baru per hari, lebih dari 80.", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    private async Task<long> SaveExamAsync(ExamBuilder exam) => (await SaveAsync(exam)).Id;

    private async Task<Domain.Content.Exam> SaveAsync(ExamBuilder exam)
    {
        await using var db = database.CreateContext();
        return await db.SaveAsync(exam.Build());
    }

    private async Task<long> GoalIdAsync(Guid userId, long examId)
    {
        await using var db = database.CreateContext();
        return await db.StudyGoals.Where(g => g.UserId == userId && g.ExamId == examId && g.ArchivedAt == null).Select(g => g.Id).SingleAsync();
    }

    /// <summary>Marks every item answered (right) and closes the attempt, as if the learner had finished it; reviewed cards move to tomorrow.</summary>
    private async Task AnswerAllAsync(long attemptId, DateTime at)
    {
        await using var db = database.CreateContext();
        var attempt = await db.Attempts.Include(a => a.Items).SingleAsync(a => a.Id == attemptId);
        foreach (var item in attempt.Items)
        {
            (item.IsAnswered, item.IsCorrect, item.AnsweredAt) = (true, true, at);
        }

        (attempt.Status, attempt.SubmittedAt, attempt.CorrectCount) = (AttemptStatus.Submitted, at, attempt.Items.Count);
        var questionIds = attempt.Items.Select(i => i.QuestionId).ToList();
        foreach (var card in await db.ReviewCards.Where(c => c.UserId == attempt.UserId && questionIds.Contains(c.QuestionId)).ToListAsync())
        {
            card.DueAt = at.AddDays(1);
        }

        await db.SaveChangesAsync();
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

    private static async Task<string> StartAsync(HttpClient client, long goalId)
    {
        using var response = await client.PostFormAsync(TodayUrl, Goal(goalId), $"{TodayUrl}?handler=Plan");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/sesi/", location, StringComparison.Ordinal);
        return location;
    }

    private static Dictionary<string, string> Goal(long goalId) => new() { ["goalId"] = goalId.ToString(CultureInfo.InvariantCulture) };

    private static Task<string> TodayAsync(HttpClient client) => client.GetStringAsync(new Uri(TodayUrl, UriKind.Relative));

    private static long IdOf(string player) => long.Parse(player.Split('/')[^1], CultureInfo.InvariantCulture);

    /// <summary>The text of the first element with this data-testid.</summary>
    private static string Testid(string html, string testId)
    {
        var start = html.IndexOf($"data-testid=\"{testId}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{testId} not found");
        start = html.IndexOf('>', start) + 1;
        return html[start..html.IndexOf('<', start)].Trim();
    }
}
