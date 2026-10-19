using System.Net;
using System.Text.Json;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Akusiap.Web.Sessions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Sessions;

/// <summary>SES-02: the setup page starts attempts within the exam's bounds and refuses everything else with a message.</summary>
[Collection(UsesDatabase.Name)]
public sealed class SessionSetupTests(PostgresFixture database)
{
    [Fact]
    public async Task A_guest_starts_a_trial_and_gets_the_guest_cookie()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var examId = await SeedAsync(TestData.Exam().Paid(49_000).WithQuestions(6, trial: 4));

        var page = WebUtility.HtmlDecode(await guest.GetStringAsync(new Uri($"/ujian/{examId}/mulai", UriKind.Relative)));
        using var started = await guest.PostFormAsync($"/ujian/{examId}/mulai", Practice(count: "4"));

        Assert.Contains("Mode coba: 4 soal gratis", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        Assert.Matches("^/sesi/[0-9]+$", started.Headers.Location!.OriginalString);
        var attempt = await AttemptAsync(started);
        Assert.Equal((AttemptSource.Trial, AttemptMode.Practice, 4, (Guid?)null), (attempt.Source, attempt.Mode, attempt.TotalCount, attempt.UserId));
        Assert.NotNull(attempt.GuestToken);
        Assert.Contains(started.Headers.GetValues("Set-Cookie"), c => c.StartsWith(GuestTokenCookie.Name + "=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Out_of_bounds_values_are_refused_with_messages_and_nothing_starts()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var preset = new RulePreset { Name = "Batas " + TestData.Unique(), TimeLimitMinutes = 60, HintsEnabled = false };
        var examId = await SeedAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(5));

        using var response = await learner.PostFormAsync($"/ujian/{examId}/mulai", Practice(count: "9", minutes: "30", hints: true));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Jumlah soal harus 1 sampai 5.", html, StringComparison.Ordinal);
        Assert.Contains("Batas waktu minimal 60 menit.", html, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.False(await db.Attempts.AnyAsync(a => a.UserId == user.Id));
    }

    [Fact]
    public async Task Practice_rules_within_bounds_are_used_and_simulation_ignores_them()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var preset = new RulePreset { Name = "Sim " + TestData.Unique(), TimeLimitMinutes = 20, QuestionCount = 3, MaxAttempts = 1 };
        var examId = await SeedAsync(TestData.Exam().Free().WithRules(preset).WithQuestions(5));

        using var practice = await learner.PostFormAsync($"/ujian/{examId}/mulai", Practice(count: "2", untimed: true, shuffle: false));
        using var simulation = await learner.PostFormAsync($"/ujian/{examId}/mulai", Practice(count: "99", minutes: "1", mode: "Simulation"));
        using var second = await learner.PostFormAsync($"/ujian/{examId}/mulai", Practice(mode: "Simulation"));
        var secondHtml = WebUtility.HtmlDecode(await second.Content.ReadAsStringAsync());

        using var practiceRules = JsonDocument.Parse((await AttemptAsync(practice)).RulesSnapshot);
        Assert.Equal(2, practiceRules.RootElement.GetProperty("questionCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, practiceRules.RootElement.GetProperty("timeLimitMinutes").ValueKind);
        Assert.False(practiceRules.RootElement.GetProperty("shuffleQuestions").GetBoolean());
        var sim = await AttemptAsync(simulation);
        using var simRules = JsonDocument.Parse(sim.RulesSnapshot);
        Assert.Equal((AttemptMode.Simulation, 3), (sim.Mode, sim.TotalCount));
        Assert.Equal(20, simRules.RootElement.GetProperty("timeLimitMinutes").GetInt32());
        Assert.Contains("Batas 1 kali simulasi untuk ujian ini sudah tercapai.", secondHtml, StringComparison.Ordinal);
        Assert.Contains("Sisa 0 kali simulasi.", secondHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Choosing_a_topic_limits_the_questions()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var exam = TestData.Exam().Free().WithQuestions(4).Build();
        var other = new Topic { Subject = exam.Subject, Name = "Topik B " + TestData.Unique() };
        exam.Questions.Where(q => q.SortOrder > 2).ToList().ForEach(q => q.Question!.Topic = other);
        var examId = await SeedAsync(exam);

        var page = await learner.GetStringAsync(new Uri($"/ujian/{examId}/mulai", UriKind.Relative));
        var form = Practice(count: "2");
        form["Form.TopicIds"] = other.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var started = await learner.PostFormAsync($"/ujian/{examId}/mulai", form);

        Assert.Contains(other.Name, page, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        var attemptId = long.Parse(started.Headers.Location!.OriginalString.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.All(await db.AttemptItems.Where(i => i.AttemptId == attemptId).Select(i => i.Question!.TopicId).ToListAsync(), id => Assert.Equal(other.Id, id));
    }

    [Fact]
    public async Task Viewers_who_cannot_start_go_to_the_exam_page_and_hidden_exams_are_not_found()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var free = await SeedAsync(TestData.Exam().Free().WithQuestions(2));
        var draft = await SeedAsync(TestData.Exam().Free().Draft().WithQuestions(2));

        using var freeForGuest = await guest.GetAsync(new Uri($"/ujian/{free}/mulai", UriKind.Relative));
        using var hidden = await guest.GetAsync(new Uri($"/ujian/{draft}/mulai", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, freeForGuest.StatusCode);
        Assert.Equal($"/ujian/{free}", freeForGuest.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    // ---------- Helpers ----------

    private static Dictionary<string, string> Practice(string? count = null, string? minutes = null, bool untimed = false, bool hints = false, bool shuffle = true, string mode = "Practice")
    {
        var form = new Dictionary<string, string>
        {
            ["Form.Mode"] = mode,
            ["Form.Untimed"] = untimed ? "true" : "false",
            ["Form.HintsEnabled"] = hints ? "true" : "false",
            ["Form.ShuffleQuestions"] = shuffle ? "true" : "false",
            ["Form.ShuffleOptions"] = shuffle ? "true" : "false",
        };
        if (count is not null)
        {
            form["Form.QuestionCount"] = count;
        }

        if (minutes is not null)
        {
            form["Form.TimeLimitMinutes"] = minutes;
        }

        return form;
    }

    private async Task<long> SeedAsync(Exam exam)
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(exam)).Id;
    }

    private async Task<Attempt> AttemptAsync(HttpResponseMessage started)
    {
        Assert.Equal(HttpStatusCode.Redirect, started.StatusCode);
        var id = long.Parse(started.Headers.Location!.OriginalString.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        await using var db = database.CreateContext();
        return await db.Attempts.SingleAsync(a => a.Id == id);
    }
}
