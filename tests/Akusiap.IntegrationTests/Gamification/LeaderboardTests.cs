using System.Text.RegularExpressions;
using Akusiap.Application.Abstractions;
using Akusiap.Application.Sessions;
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

/// <summary>GAM-01: public exam leaderboards count each person's first finished Simulation, by score then time.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class LeaderboardTests(PostgresFixture database)
{
    [Fact]
    public async Task Only_the_first_simulation_counts_and_ties_go_to_the_faster_time()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var viewer = TestData.User().Named("Penonton").Build();
        using var client = await app.CreateSignedInClientAsync(viewer, RoleNames.Learner);
        var (ana, budi, citra, dewi) = (Named("Ana"), Named("Budi"), Named("Citra"), Named("Dewi"));
        dewi.HideOnLeaderboard = true;
        long examId;
        await using (var db = database.CreateContext())
        {
            foreach (var user in new[] { ana, budi, citra, dewi })
            {
                await db.SaveAsync(user);
            }

            examId = (await db.SaveAsync(TestData.Exam().Free().WithQuestions(2).Build())).Id;
            var t0 = TestData.Now;
            db.Attempts.AddRange(
                Attempt(ana.Id, examId, 80m, 600, t0),
                Attempt(ana.Id, examId, 100m, 300, t0.AddDays(1)), // a later retake: never counts
                Attempt(budi.Id, examId, 80m, 400, t0.AddHours(1)), // same score as Ana, faster
                Attempt(citra.Id, examId, 95m, 900, t0.AddHours(2), mode: AttemptMode.Practice), // practice: never counts
                Attempt(citra.Id, examId, 60m, 900, t0.AddHours(3)),
                Attempt(dewi.Id, examId, 90m, 1200, t0.AddHours(4)),
                Attempt(viewer.Id, examId, 70m, 500, t0.AddHours(5), source: AttemptSource.Trial)); // trial: never counts
            await db.SaveChangesAsync();
        }

        var html = await client.GetStringAsync(new Uri($"/ujian/{examId}/peringkat", UriKind.Relative));

        Assert.Equal(["Peserta #" + dewi.Id.ToString("N")[..4].ToUpperInvariant(), "Budi", "Ana", "Citra"], NamePattern().Matches(html).Select(m => m.Groups[1].Value));
        Assert.DoesNotContain("Dewi", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-viewer=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Finishing_a_counted_simulation_awards_a_rank_badge_and_shows_the_rank()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var rival = TestData.User().Build();
        long examId, attemptId;
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(rival);
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(2).Build());
            examId = exam.Id;
            db.Attempts.Add(Attempt(rival.Id, examId, 50m, 300, TestData.Now.AddHours(-1)));
            var attempt = new Attempt
            {
                UserId = user.Id, ExamId = examId, Source = AttemptSource.Exam, Mode = AttemptMode.Simulation, RulesSnapshot = "{}",
                StartedAt = app.Clock.UtcNow.AddMinutes(-5), LastSavedAt = app.Clock.UtcNow,
            };
            var position = 1;
            foreach (var q in exam.Questions.OrderBy(q => q.SortOrder))
            {
                var item = new AttemptItem { QuestionId = q.QuestionId, Position = position++, IsAnswered = true };
                item.SelectedOptions.Add(new AttemptItemOption { QuestionOptionId = q.Question!.Options.Single(o => o.IsCorrect).Id });
                attempt.Items.Add(item);
            }

            db.Attempts.Add(attempt);
            await db.SaveChangesAsync();
            attemptId = attempt.Id;
        }

        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var attempt = await db.Attempts.SingleAsync(a => a.Id == attemptId);
            await scope.ServiceProvider.GetRequiredService<AttemptFinisher>().FinishAsync(attempt, AttemptStatus.Submitted, app.Clock.UtcNow, CancellationToken.None);
        }

        var result = await client.GetStringAsync(new Uri($"/sesi/{attemptId}/hasil", UriKind.Relative));
        var board = await client.GetStringAsync(new Uri($"/ujian/{examId}/peringkat", UriKind.Relative));

        Assert.Contains("data-testid=\"result-rank\"", result, StringComparison.Ordinal);
        Assert.Contains("Peringkat #1 dari 2 peserta", result, StringComparison.Ordinal);
        Assert.Contains("data-rank=\"1\" data-viewer=\"true\"", board, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        var badges = await check.UserBadges.Where(b => b.UserId == user.Id).ToListAsync();
        Assert.Contains(badges, b => b.Code == BadgeCode.Rank1 && b.ExamId == examId);
        Assert.Contains(badges, b => b.Code == BadgeCode.FirstSteps && b.ExamId == null);
        Assert.Contains(badges, b => b.Code == BadgeCode.Sharpshooter);
    }

    [Fact]
    public async Task Own_exams_have_no_public_board_but_the_owner_sees_participants()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var owner = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(owner, RoleNames.Learner);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync(TestData.Exam().OwnedBy(owner).WithQuestions(2).Build())).Id;
            db.Attempts.Add(new Attempt
            {
                GuestToken = Guid.NewGuid(), GuestName = "Tamu Rina", ExamId = examId, Source = AttemptSource.Shared, Mode = AttemptMode.Practice,
                Status = AttemptStatus.Submitted, RulesSnapshot = "{}", StartedAt = TestData.Now, LastSavedAt = TestData.Now, SubmittedAt = TestData.Now,
                ScorePercent = 50m, CorrectCount = 1, TotalCount = 2, TimeSpentSeconds = 75,
            });
            await db.SaveChangesAsync();
        }

        using var board = await client.GetAsync(new Uri($"/ujian/{examId}/peringkat", UriKind.Relative));
        var participants = await client.GetStringAsync(new Uri($"/learn/ujian-saya/{examId}/peserta", UriKind.Relative));

        Assert.Equal(System.Net.HttpStatusCode.NotFound, board.StatusCode);
        Assert.Contains("Tamu Rina", participants, StringComparison.Ordinal);
        Assert.Contains("1:15", participants, StringComparison.Ordinal);
    }

    private static User Named(string name) => TestData.User().Named(name).Build();

    private static Attempt Attempt(Guid userId, long examId, decimal score, int seconds, DateTime submittedAt, AttemptMode mode = AttemptMode.Simulation, AttemptSource source = AttemptSource.Exam) => new()
    {
        UserId = userId,
        ExamId = examId,
        Source = source,
        Mode = mode,
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

    [GeneratedRegex("data-testid=\"leaderboard-name\">([^<]*)<")]
    private static partial Regex NamePattern();
}
