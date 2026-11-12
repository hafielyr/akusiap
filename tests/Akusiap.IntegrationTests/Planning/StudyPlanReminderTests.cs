using System.Globalization;
using System.Net;
using Akusiap.Domain;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Notifications;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Akusiap.Web.Planning;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Planning;

/// <summary>PLN-05 (D-126): the daily plan reminder email, at most once per Jakarta day, with a profile switch.</summary>
[Collection(UsesDatabase.Name)]
public sealed class StudyPlanReminderTests(PostgresFixture database)
{
    [Fact]
    public async Task One_email_a_day_lists_the_new_questions_cards_and_days_left()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Site:BaseUrl", "https://akusiap.test/"));
        var user = TestData.User().Build();
        var unconfirmed = TestData.User().Unconfirmed().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(unconfirmed, RoleNames.Learner);
        var exam = await SaveExamAsync(TestData.Exam().Titled($"SKD {TestData.Unique()}").Free().WithQuestions(20));
        await using (var db = database.CreateContext())
        {
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = exam.Questions.First().QuestionId, State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-1) });
            await db.SaveChangesAsync();
        }

        var examDate = JakartaTime.Today(app.Clock.UtcNow).AddDays(12);
        await SetGoalAsync(learner, exam.Id, examDate);
        await SetGoalAsync(other, exam.Id, examDate);

        // 12 days: 2 review days, 10 learning days, ceil(20 / 10) = 2 new questions; plus 1 due card.
        await RunAsync(site);
        await RunAsync(site);
        var firstDay = app.Email.To(user.Email!);
        app.Clock.Advance(TimeSpan.FromDays(1));
        await RunAsync(site);

        var mail = Assert.Single(firstDay);
        Assert.Equal("Tugas hari ini: 2 soal baru dan 1 kartu ulasan", mail.Subject);
        Assert.Contains($"{exam.Title} (H-12): 2 soal baru, 1 kartu ulasan", mail.TextBody!, StringComparison.Ordinal);
        Assert.Contains("https://akusiap.test/learn/hari-ini", mail.TextBody!, StringComparison.Ordinal);
        Assert.Contains("https://akusiap.test/learn/profil", mail.TextBody!, StringComparison.Ordinal);
        Assert.Equal(2, app.Email.To(user.Email!).Count); // the next day sends again
        Assert.Empty(app.Email.To(unconfirmed.Email!));
        await using var check = database.CreateContext();
        Assert.Equal(2, await check.Notifications.CountAsync(n => n.UserId == user.Id && n.TemplateCode == NotificationTemplate.ReviewDue && n.Status == NotificationStatus.Sent));
    }

    [Fact]
    public async Task The_profile_switch_stops_it_and_nothing_planned_sends_nothing()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Site:BaseUrl", "https://akusiap.test/"));
        var optedOut = TestData.User().Build();
        var idle = TestData.User().Build();
        using var optedOutClient = await app.CreateSignedInClientAsync(optedOut, RoleNames.Learner);
        using var idleClient = await app.CreateSignedInClientAsync(idle, RoleNames.Learner);
        var exam = await SaveExamAsync(TestData.Exam().Free().WithQuestions(20));
        var examDate = JakartaTime.Today(app.Clock.UtcNow).AddDays(12);
        await SetGoalAsync(optedOutClient, exam.Id, examDate);
        await SetGoalAsync(idleClient, exam.Id, examDate);

        using var saved = await optedOutClient.PostFormAsync("/learn/profil", new Dictionary<string, string>
        {
            ["Input.DisplayName"] = "Tanpa Email",
            ["Input.PhoneNumber"] = string.Empty,
            ["Input.PreferredLanguage"] = Languages.Indonesian,
            ["Input.IsTeacher"] = "false",
            ["Input.PlanReminderEmails"] = "false",
        }, "/learn/profil?handler=Profile");
        app.Clock.Advance(TimeSpan.FromDays(10)); // the review phase, and the idle learner has no due cards
        await RunAsync(site);

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.DoesNotContain("checked", await ProfileSwitchAsync(optedOutClient), StringComparison.Ordinal);
        Assert.Empty(app.Email.To(optedOut.Email!));
        Assert.Empty(app.Email.To(idle.Email!));
    }

    private static async Task<string> ProfileSwitchAsync(HttpClient client)
    {
        var html = await client.GetStringAsync(new Uri("/learn/profil", UriKind.Relative));
        var start = html.LastIndexOf("<input", html.IndexOf("data-testid=\"plan-reminder-emails\"", StringComparison.Ordinal), StringComparison.Ordinal);
        return html[start..html.IndexOf('>', start)];
    }

    private async Task<Domain.Content.Exam> SaveExamAsync(ExamBuilder exam)
    {
        await using var db = database.CreateContext();
        return await db.SaveAsync(exam.Build());
    }

    private static async Task RunAsync(WebApplicationFactory<Program> site)
    {
        await using var scope = site.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<StudyPlanReminderJob>().RunAsync(CancellationToken.None);
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
