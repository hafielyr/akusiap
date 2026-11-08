using System.Globalization;
using System.Net;
using Akusiap.Domain;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Planning;

/// <summary>PLN-01 (D-119, D-120): exam goals on <c>/learn/target</c> and the countdown on the Today page.</summary>
[Collection(UsesDatabase.Name)]
public sealed class StudyGoalTests(PostgresFixture database)
{
    private const string GoalsUrl = "/learn/target";
    private const string TodayUrl = "/learn/hari-ini";

    [Fact]
    public async Task A_learner_sets_a_goal_and_the_countdown_follows_the_clock()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Titled($"CPNS {TestData.Unique()}").Free().WithQuestions(3).Build())).Id;
        }

        var today = JakartaTime.Today(app.Clock.UtcNow);
        var before = await learner.GetStringAsync(new Uri(TodayUrl, UriKind.Relative));
        using var saved = await SetGoalAsync(learner, examId, today.AddDays(23));
        var after = await learner.GetStringAsync(new Uri(TodayUrl, UriKind.Relative));
        app.Clock.Advance(TimeSpan.FromDays(1));
        var nextDay = await learner.GetStringAsync(new Uri(TodayUrl, UriKind.Relative));

        Assert.Contains("data-testid=\"goal-set-link\"", before, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(GoalsUrl, saved.Headers.Location!.OriginalString);
        Assert.Contains($"data-testid=\"goal-countdown\" data-exam=\"{examId}\"", after, StringComparison.Ordinal);
        Assert.Contains(">H-23</span>", after, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"goal-set-link\"", after, StringComparison.Ordinal);
        Assert.Contains(">H-22</span>", nextDay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_exam_day_shows_hari_h_and_a_passed_goal_leaves_the_today_page()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build())).Id;
        }

        using var saved = await SetGoalAsync(learner, examId, JakartaTime.Today(app.Clock.UtcNow).AddDays(1));
        app.Clock.Advance(TimeSpan.FromDays(1));
        var examDay = await learner.GetStringAsync(new Uri(TodayUrl, UriKind.Relative));
        app.Clock.Advance(TimeSpan.FromDays(1));
        var dayAfter = await learner.GetStringAsync(new Uri(TodayUrl, UriKind.Relative));
        var goalsPage = await learner.GetStringAsync(new Uri(GoalsUrl, UriKind.Relative));

        Assert.Contains(">Hari H</span>", examDay, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"goal-countdown\"", dayAfter, StringComparison.Ordinal);
        Assert.Contains("Sudah lewat", goalsPage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "Pilih tanggal setelah hari ini.")]
    [InlineData(-3, "Pilih tanggal setelah hari ini.")]
    [InlineData(731, "Pilih tanggal paling lambat 2 tahun dari sekarang.")]
    public async Task Dates_not_after_today_or_more_than_two_years_ahead_are_refused(int daysFromToday, string message)
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build())).Id;
        }

        var today = JakartaTime.Today(app.Clock.UtcNow);
        var date = daysFromToday == 731 ? today.AddYears(2).AddDays(1) : today.AddDays(daysFromToday);
        using var response = await SetGoalAsync(learner, examId, date);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, html, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        Assert.False(await check.StudyGoals.AnyAsync(g => g.UserId == user.Id));
    }

    [Fact]
    public async Task A_missing_date_is_refused()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build())).Id;
        }

        using var response = await learner.PostFormAsync(GoalsUrl, new Dictionary<string, string>
        {
            ["Input.ExamId"] = examId.ToString(CultureInfo.InvariantCulture),
            ["Input.ExamDate"] = string.Empty,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Isi tanggal ujian.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_published_catalog_exams_the_learner_can_start_are_accepted()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long draft, paidWithoutTrial, paidWithTrial;
        await using (var db = database.CreateContext())
        {
            draft = (await db.SaveAsync(TestData.Exam().Free().Draft().WithQuestions(1).Build())).Id;
            paidWithoutTrial = (await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(3).Build())).Id;
            paidWithTrial = (await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(3, trial: 2).Build())).Id;
        }

        var date = JakartaTime.Today(app.Clock.UtcNow).AddDays(30);
        var refused = new List<HttpStatusCode>();
        foreach (var examId in new[] { draft, paidWithoutTrial, long.MaxValue })
        {
            using var response = await SetGoalAsync(learner, examId, date);
            Assert.Contains("data-testid=\"error-exam\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            refused.Add(response.StatusCode);
        }

        using var trial = await SetGoalAsync(learner, paidWithTrial, date);
        var options = await learner.GetStringAsync(new Uri(GoalsUrl, UriKind.Relative));

        Assert.All(refused, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(HttpStatusCode.Redirect, trial.StatusCode); // trial access is enough (D-119)
        Assert.Contains($"<option value=\"{paidWithTrial}\"", options, StringComparison.Ordinal);
        Assert.DoesNotContain($"<option value=\"{paidWithoutTrial}\"", options, StringComparison.Ordinal);
        Assert.DoesNotContain($"<option value=\"{draft}\"", options, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        Assert.Equal([paidWithTrial], await check.StudyGoals.Where(g => g.UserId == user.Id).Select(g => g.ExamId).ToListAsync());
    }

    [Fact]
    public async Task Setting_the_same_exam_again_moves_the_date_and_removing_hides_the_goal()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build())).Id;
        }

        var today = JakartaTime.Today(app.Clock.UtcNow);
        (await SetGoalAsync(learner, examId, today.AddDays(40))).Dispose();
        (await SetGoalAsync(learner, examId, today.AddDays(10))).Dispose();

        long goalId;
        await using (var db = database.CreateContext())
        {
            var goal = await db.StudyGoals.SingleAsync(g => g.UserId == user.Id);
            Assert.Equal(today.AddDays(10), goal.ExamDate);
            goalId = goal.Id;
        }

        var removeUrl = $"{GoalsUrl}?handler=Remove&id={goalId}";
        using var notTheirs = await other.PostFormAsync(GoalsUrl, new Dictionary<string, string>(), removeUrl);
        using var removed = await learner.PostFormAsync(GoalsUrl, new Dictionary<string, string>(), removeUrl);
        var todayHtml = await learner.GetStringAsync(new Uri(TodayUrl, UriKind.Relative));
        var goalsHtml = await learner.GetStringAsync(new Uri(GoalsUrl, UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, notTheirs.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, removed.StatusCode);
        Assert.DoesNotContain("data-testid=\"goal-countdown\"", todayHtml, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"goals-none\"", goalsHtml, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        Assert.NotNull((await check.StudyGoals.SingleAsync(g => g.Id == goalId)).ArchivedAt);
    }

    [Fact]
    public async Task The_exam_page_links_signed_in_learners_to_their_goal()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(3, trial: 2).Build())).Id;
        }

        var examUrl = new Uri($"/ujian/{examId}", UriKind.Relative);
        var guestHtml = await guest.GetStringAsync(examUrl);
        var beforeHtml = await learner.GetStringAsync(examUrl);
        (await SetGoalAsync(learner, examId, JakartaTime.Today(app.Clock.UtcNow).AddDays(5))).Dispose();

        var afterHtml = await learner.GetStringAsync(examUrl);
        var preselected = await learner.GetStringAsync(new Uri($"{GoalsUrl}?examId={examId}", UriKind.Relative));

        Assert.DoesNotContain("data-testid=\"goal-link\"", guestHtml, StringComparison.Ordinal);
        Assert.Contains("Pasang target ujian", beforeHtml, StringComparison.Ordinal);
        Assert.Contains($"href=\"/learn/target?examId={examId}\"", beforeHtml, StringComparison.Ordinal);
        Assert.Contains(">H-5</span>", afterHtml, StringComparison.Ordinal);
        Assert.Contains($"<option value=\"{examId}\" selected=\"selected\"", preselected, StringComparison.Ordinal);
    }

    private static Task<HttpResponseMessage> SetGoalAsync(HttpClient client, long examId, DateOnly date) =>
        client.PostFormAsync(GoalsUrl, new Dictionary<string, string>
        {
            ["Input.ExamId"] = examId.ToString(CultureInfo.InvariantCulture),
            ["Input.ExamDate"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        });
}
