using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Application.Gamification;
using Akusiap.Domain.Content;
using Akusiap.Domain.Gamification;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Gamification;

/// <summary>
/// DRL-02: admins manage each catalog exam's leaderboard (on/off, weekly reset, badges, minimums, history reset).
/// The test clock starts on Thursday 1 Oct 2026 (09:00 WIB), so "this week" starts Monday 28 Sep.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed partial class LeaderboardManagementTests(PostgresFixture database)
{
    private static readonly DateOnly ThisWeek = new(2026, 9, 28);

    [Fact]
    public async Task Admin_saves_the_settings_and_learners_cannot_open_the_page()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var examId = await SeedExamAsync();

        using var saved = await admin.PostFormAsync(EditUrl(examId), Form(weekly: true, top10: false, minScore: "60", minPeople: "3"));
        using var invalid = await admin.PostFormAsync(EditUrl(examId), Form(minScore: "150"));
        var invalidHtml = WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync());
        var list = await admin.GetStringAsync(new Uri("/admin/papan-peringkat", UriKind.Relative));
        using var forbidden = await learner.GetAsync(new Uri("/admin/papan-peringkat", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("harus 0 sampai 100.", invalidHtml, StringComparison.Ordinal);
        Assert.Contains($"data-exam=\"{examId}\"", list, StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, forbidden.StatusCode);
        await using var db = database.CreateContext();
        var settings = await db.LeaderboardSettings.SingleAsync(s => s.ExamId == examId);
        Assert.Equal((true, true, true, true, false, (int?)60, (int?)3), (settings.Enabled, settings.WeeklyReset, settings.AwardRank1, settings.AwardTop3, settings.AwardTop10, settings.MinScorePercent, settings.MinParticipants));
        Assert.Equal(ThisWeek.AddDays(-7), settings.WeeklyAwardedThrough); // earlier weeks never give weekly badges
    }

    [Fact]
    public async Task A_switched_off_board_is_hidden_and_gives_no_rank()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var user = await SeedUserAsync("Ana");
        var examId = await SeedExamAsync();
        var attemptId = await SeedAttemptAsync(user, examId, 90m, 300, app.Clock.UtcNow.AddHours(-1));

        using var saved = await admin.PostFormAsync(EditUrl(examId), Form(enabled: false));
        var detail = await admin.GetStringAsync(new Uri($"/ujian/{examId}", UriKind.Relative));
        using var board = await admin.GetAsync(new Uri($"/ujian/{examId}/peringkat", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.DoesNotContain("data-testid=\"leaderboard-link\"", detail, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, board.StatusCode);
        await using var scope = app.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<Leaderboards>().StandingAsync(attemptId, CancellationToken.None));
        await scope.ServiceProvider.GetRequiredService<Badges>().AwardAsync(user.Id, attemptId, CancellationToken.None);
        await using var db = database.CreateContext();
        Assert.False(await db.UserBadges.AnyAsync(b => b.UserId == user.Id && b.ExamId == examId));
    }

    [Fact]
    public async Task A_weekly_board_counts_this_week_and_the_all_time_tab_counts_everything()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (ana, budi) = (await SeedUserAsync("Ana"), await SeedUserAsync("Budi"));
        var examId = await SeedExamAsync();
        var now = app.Clock.UtcNow;
        await SeedAttemptAsync(ana, examId, 100m, 300, now.AddDays(-6)); // last week
        await SeedAttemptAsync(ana, examId, 50m, 300, now.AddHours(-3)); // Ana's first attempt this week
        var budiAttempt = await SeedAttemptAsync(budi, examId, 80m, 400, now.AddHours(-2));
        await admin.PostFormAsync(EditUrl(examId), Form(weekly: true));

        var week = await admin.GetStringAsync(new Uri($"/ujian/{examId}/peringkat", UriKind.Relative));
        var allTime = await admin.GetStringAsync(new Uri($"/ujian/{examId}/peringkat?periode=semua", UriKind.Relative));

        Assert.Contains("data-testid=\"period-week\"", week, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"resets-at\"", week, StringComparison.Ordinal);
        Assert.Equal(["Budi", "Ana"], Names(week));
        Assert.Equal(["Ana", "Budi"], Names(allTime));
        await using var scope = app.Services.CreateAsyncScope();
        var standing = await scope.ServiceProvider.GetRequiredService<Leaderboards>().StandingAsync(budiAttempt, CancellationToken.None);
        Assert.Equal((1, 2, true, (BadgeCode?)null), (standing!.Rank, standing.Total, standing.Weekly, standing.Badge));
        await scope.ServiceProvider.GetRequiredService<Badges>().AwardAsync(budi.Id, budiAttempt, CancellationToken.None);
        await using var db = database.CreateContext();
        Assert.False(await db.UserBadges.AnyAsync(b => b.UserId == budi.Id && b.ExamId == examId)); // weekly badges wait for the week to end
    }

    [Fact]
    public async Task Weekly_badges_go_to_the_final_ranks_when_the_week_ends()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var ana = TestData.User().Named("Ana").Build();
        using var anaClient = await app.CreateSignedInClientAsync(ana, RoleNames.Learner);
        var (budi, citra, dewi) = (await SeedUserAsync("Budi"), await SeedUserAsync("Citra"), await SeedUserAsync("Dewi"));
        var examId = await SeedExamAsync();
        var now = app.Clock.UtcNow;
        await SeedAttemptAsync(ana, examId, 90m, 300, now.AddHours(-4));
        await SeedAttemptAsync(budi, examId, 70m, 300, now.AddHours(-3));
        await SeedAttemptAsync(citra, examId, 65m, 300, now.AddHours(-2));
        await SeedAttemptAsync(dewi, examId, 50m, 300, now.AddHours(-1)); // below the minimum score
        await admin.PostFormAsync(EditUrl(examId), Form(weekly: true, top10: false, minScore: "60", minPeople: "3"));

        var midWeek = await RunAwardsAsync(app);
        app.Clock.UtcNow = new DateTime(2026, 10, 4, 17, 30, 0, DateTimeKind.Utc); // Monday 5 Oct 00:30 WIB
        var weekEnd = await RunAwardsAsync(app);
        var again = await RunAwardsAsync(app);
        await SeedAttemptAsync(ana, examId, 95m, 300, app.Clock.UtcNow.AddDays(1)); // next week only Ana takes part
        app.Clock.UtcNow = app.Clock.UtcNow.AddDays(7);
        var nextWeek = await RunAwardsAsync(app);
        var journey = await anaClient.GetStringAsync(new Uri("/learn/perjalanan", UriKind.Relative));

        Assert.Equal((0, 3, 0, 0), (midWeek, weekEnd, again, nextWeek));
        await using var db = database.CreateContext();
        var badges = await db.UserBadges.Where(b => b.ExamId == examId).ToListAsync();
        Assert.All(badges, b => Assert.Equal(ThisWeek, b.WeekStart));
        Assert.Equal(BadgeCode.Rank1, badges.Single(b => b.UserId == ana.Id).Code);
        Assert.Equal(BadgeCode.Top3, badges.Single(b => b.UserId == budi.Id).Code);
        Assert.Equal(BadgeCode.Top3, badges.Single(b => b.UserId == citra.Id).Code);
        Assert.DoesNotContain(badges, b => b.UserId == dewi.Id);
        Assert.Equal(ThisWeek.AddDays(7), (await db.LeaderboardSettings.SingleAsync(s => s.ExamId == examId)).WeeklyAwardedThrough);
        Assert.Contains("data-week=\"2026-09-28\"", journey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Resetting_the_history_starts_the_board_from_now()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (ana, budi) = (await SeedUserAsync("Ana"), await SeedUserAsync("Budi"));
        var examId = await SeedExamAsync();
        var anaAttempt = await SeedAttemptAsync(ana, examId, 100m, 300, app.Clock.UtcNow.AddHours(-1));

        using var reset = await admin.PostFormAsync(EditUrl(examId), new Dictionary<string, string>(), $"{EditUrl(examId)}?handler=Reset");
        var empty = await admin.GetStringAsync(new Uri($"/ujian/{examId}/peringkat", UriKind.Relative));
        await SeedAttemptAsync(budi, examId, 60m, 300, app.Clock.UtcNow.AddMinutes(30));
        var after = await admin.GetStringAsync(new Uri($"/ujian/{examId}/peringkat", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Contains("data-testid=\"leaderboard-empty\"", empty, StringComparison.Ordinal);
        Assert.Equal(["Budi"], Names(after));
        await using var db = database.CreateContext();
        Assert.True(await db.Attempts.AnyAsync(a => a.Id == anaAttempt)); // history stays
    }

    [Fact]
    public async Task Badge_choices_and_minimums_decide_the_rank_badge()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (ana, budi) = (await SeedUserAsync("Ana"), await SeedUserAsync("Budi"));
        var noRank1 = await SeedExamAsync();
        var highBar = await SeedExamAsync();
        var anaFirst = await SeedAttemptAsync(ana, noRank1, 80m, 300, app.Clock.UtcNow.AddHours(-2));
        await SeedAttemptAsync(budi, noRank1, 70m, 300, app.Clock.UtcNow.AddHours(-1));
        var anaHigh = await SeedAttemptAsync(ana, highBar, 80m, 300, app.Clock.UtcNow.AddHours(-1));
        await admin.PostFormAsync(EditUrl(noRank1), Form(rank1: false));
        await admin.PostFormAsync(EditUrl(highBar), Form(minScore: "90"));

        await using var scope = app.Services.CreateAsyncScope();
        var leaderboards = scope.ServiceProvider.GetRequiredService<Leaderboards>();
        var first = await leaderboards.StandingAsync(anaFirst, CancellationToken.None);
        var high = await leaderboards.StandingAsync(anaHigh, CancellationToken.None);
        var prizes = await admin.GetStringAsync(new Uri($"/ujian/{highBar}/peringkat", UriKind.Relative));

        Assert.Equal((1, BadgeCode.Top3), (first!.Rank, first.Badge));
        Assert.Equal((1, (BadgeCode?)null), (high!.Rank, high.Badge));
        Assert.Contains("nilai min. 90%", prizes, StringComparison.Ordinal);
    }

    // ---------- Helpers ----------

    private static string EditUrl(long examId) => $"/admin/papan-peringkat/{examId}";

    private static Dictionary<string, string> Form(bool enabled = true, bool weekly = false, bool rank1 = true, bool top3 = true, bool top10 = true, string minScore = "", string minPeople = "")
    {
        var form = new Dictionary<string, string> { ["Input.MinScorePercent"] = minScore, ["Input.MinParticipants"] = minPeople };
        foreach (var (field, on) in new[] { ("Enabled", enabled), ("WeeklyReset", weekly), ("AwardRank1", rank1), ("AwardTop3", top3), ("AwardTop10", top10) })
        {
            if (on)
            {
                form[$"Input.{field}"] = "true";
            }
        }

        return form;
    }

    private static async Task<int> RunAwardsAsync(AppFactory app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<WeeklyLeaderboardAwards>().RunAsync(CancellationToken.None);
    }

    private static string[] Names(string html) => [.. NamePattern().Matches(html).Select(m => m.Groups[1].Value)];

    private async Task<User> SeedUserAsync(string name)
    {
        await using var db = database.CreateContext();
        return await db.SaveAsync(TestData.User().Named(name).Build());
    }

    private async Task<long> SeedExamAsync()
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(TestData.Exam().Free().WithQuestions(2).Build())).Id;
    }

    private async Task<long> SeedAttemptAsync(User user, long examId, decimal score, int seconds, DateTime submittedAt)
    {
        await using var db = database.CreateContext();
        var attempt = new Attempt
        {
            UserId = user.Id,
            ExamId = examId,
            Source = AttemptSource.Exam,
            Mode = AttemptMode.Simulation,
            Status = AttemptStatus.Submitted,
            RulesSnapshot = "{}",
            StartedAt = submittedAt.AddSeconds(-seconds),
            LastSavedAt = submittedAt,
            SubmittedAt = submittedAt,
            ScorePercent = score,
            TimeSpentSeconds = seconds,
            CorrectCount = (int)(score / 10),
            TotalCount = 10,
        };
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }

    [GeneratedRegex("data-testid=\"leaderboard-name\">([^<]*)<")]
    private static partial Regex NamePattern();
}
