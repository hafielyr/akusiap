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

/// <summary>TRY-01: a Simulation on a CAT preset opens the full-screen tryout layout; Practice keeps the normal player.</summary>
[Collection(UsesDatabase.Name)]
public sealed class CatTryoutTests(PostgresFixture database)
{
    [Fact]
    public async Task A_simulation_on_a_cat_preset_opens_the_cat_screen_without_help()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(CatPreset());

        var player = await StartAsync(learner, examId, "Simulation");
        var html = await learner.GetStringAsync(new Uri(player, UriKind.Relative));

        Assert.Contains("data-cat=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"grid-cell\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"timer\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"confirm-finish\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"check\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"hint-button\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<header class=\"sticky top-0 z-30 border-b", html, StringComparison.Ordinal); // site header hidden
    }

    [Fact]
    public async Task Practice_on_a_cat_preset_keeps_the_normal_player()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(CatPreset());

        var player = await StartAsync(learner, examId, "Practice");
        var html = await learner.GetStringAsync(new Uri(player, UriKind.Relative));

        Assert.DoesNotContain("data-cat=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"player\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opening_the_cat_screen_after_the_deadline_closes_the_attempt()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedAsync(CatPreset());
        var player = await StartAsync(learner, examId, "Simulation");
        app.Clock.Advance(TimeSpan.FromMinutes(11));

        using var response = await learner.GetAsync(new Uri(player, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"{player}/hasil", response.Headers.Location!.OriginalString);
        await using var db = database.CreateContext();
        var attempt = await db.Attempts.SingleAsync(a => a.Id == IdOf(player));
        Assert.NotEqual(AttemptStatus.InProgress, attempt.Status);
    }

    [Fact]
    public async Task Admin_turns_on_the_cat_screen_for_a_preset()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var name = $"CAT {TestData.Unique()}";

        using var response = await admin.PostFormAsync("/admin/aturan/baru", new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.TimeLimitMinutes"] = "100",
            ["Input.QuestionCount"] = "",
            ["Input.MaxAttempts"] = "",
            ["Input.PassMarkPercent"] = "",
            ["Input.ShowAnswers"] = nameof(ShowAnswers.AfterSubmit),
            ["Input.CatMode"] = "true",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = database.CreateContext();
        Assert.True((await db.RulePresets.SingleAsync(r => r.Name == name)).CatMode);
    }

    // ---------- Helpers ----------

    private static Exam CatPreset() => TestData.Exam().Free()
        .WithRules(new RulePreset { Name = "CAT " + TestData.Unique(), CatMode = true, TimeLimitMinutes = 10, HintsEnabled = true, ShuffleQuestions = false, ShuffleOptions = false })
        .WithQuestions(3)
        .Build();

    private async Task<long> SeedAsync(Exam exam)
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(exam)).Id;
    }

    private static long IdOf(string player) => long.Parse(player.Split('/')[^1], CultureInfo.InvariantCulture);

    private static async Task<string> StartAsync(HttpClient client, long examId, string mode)
    {
        var form = new Dictionary<string, string>
        {
            ["Form.Mode"] = mode,
            ["Form.Untimed"] = "false",
            ["Form.TimeLimitMinutes"] = "10",
            ["Form.HintsEnabled"] = "true",
            ["Form.ShuffleQuestions"] = "false",
            ["Form.ShuffleOptions"] = "false",
        };
        using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.OriginalString;
    }
}
