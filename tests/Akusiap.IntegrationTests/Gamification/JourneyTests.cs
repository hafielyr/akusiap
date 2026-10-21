using Akusiap.Domain.Gamification;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Gamification;

/// <summary>GAM-01: the learning journey shows level, streak, totals and badges from what the learner did.</summary>
[Collection(UsesDatabase.Name)]
public sealed class JourneyTests(PostgresFixture database)
{
    [Fact]
    public async Task The_journey_counts_activity_streaks_and_awards_milestones_once()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var now = app.Clock.UtcNow;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
            // Finished sessions yesterday and today (Jakarta days), plus flashcard reviews three days ago.
            foreach (var at in new[] { now.AddDays(-1), now })
            {
                db.Attempts.Add(new Attempt
                {
                    UserId = user.Id, ExamId = exam.Id, Source = AttemptSource.Exam, Mode = AttemptMode.Practice, Status = AttemptStatus.Submitted,
                    RulesSnapshot = "{}", StartedAt = at.AddMinutes(-10), LastSavedAt = at, SubmittedAt = at, ScorePercent = 80m, CorrectCount = 8, TotalCount = 10,
                });
            }

            var card = new ReviewCard { UserId = user.Id, QuestionId = exam.Questions.Single().QuestionId, State = ReviewCardState.Review, DueAt = now.AddDays(3) };
            for (var i = 0; i < 3; i++)
            {
                card.Logs.Add(new ReviewLog { Rating = ReviewRating.Good, ReviewedAt = now.AddDays(-2).AddMinutes(i) });
            }

            db.ReviewCards.Add(card);
            await db.SaveChangesAsync();
        }

        var html = await client.GetStringAsync(new Uri("/learn/perjalanan", UriKind.Relative));
        var again = await client.GetStringAsync(new Uri("/learn/perjalanan", UriKind.Relative));
        var today = await client.GetStringAsync(new Uri("/learn/hari-ini", UriKind.Relative));

        // XP: 16 correct × 10 + 2 sessions × 20 + 3 reviews × 2 + badges (FirstSteps, Streak3) × 50 = 306 → level 3.
        Assert.Contains("data-testid=\"level\">3<", html, StringComparison.Ordinal);
        Assert.Contains("306 XP", html, StringComparison.Ordinal);
        Assert.Contains("🔥 3", html, StringComparison.Ordinal);
        Assert.Contains("data-code=\"FirstSteps\" data-earned=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-code=\"Streak3\" data-earned=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-code=\"Streak7\" data-earned=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("306 XP", again, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"today-level\">3<", today, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        Assert.Equal(2, await check.UserBadges.CountAsync(b => b.UserId == user.Id));
    }
}
