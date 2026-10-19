using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Domain.Access;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Memorization;

/// <summary>MEM-03: flashcard sets of 10 (due first), rated with SM-2, ending in a summary; only fully accessible questions.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class FlashcardTests(PostgresFixture database)
{
    [Fact]
    public async Task A_set_ends_after_10_cards_with_a_summary_and_due_cards_come_first()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        List<long> questions;
        long examId;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(12).Build());
            examId = exam.Id;
            questions = [.. exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId)];
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = questions[11], State = ReviewCardState.Review, DueAt = app.Clock.UtcNow.AddDays(-1), IntervalDays = 6, Repetitions = 2 });
            await db.SaveChangesAsync();
        }

        var set = await StartAsync(learner, examId.ToString(CultureInfo.InvariantCulture));
        var seen = new List<int>();
        for (var i = 0; i < 10; i++)
        {
            var page = await learner.GetStringAsync(new Uri(set, UriKind.Relative));
            var position = int.Parse(PositionPattern().Match(page).Groups[1].Value, CultureInfo.InvariantCulture);
            seen.Add(position);
            using var rated = await learner.PostFormAsync(set, new Dictionary<string, string>
            {
                ["position"] = position.ToString(CultureInfo.InvariantCulture), ["rating"] = i == 0 ? nameof(ReviewRating.Forgot) : nameof(ReviewRating.Good),
            }, $"{set}?handler=Rate");
            Assert.Equal(HttpStatusCode.Redirect, rated.StatusCode);
        }

        var summary = await learner.GetStringAsync(new Uri(set, UriKind.Relative));

        Assert.Equal(Enumerable.Range(1, 10), seen);
        Assert.Contains("data-testid=\"flashcard-summary\"", summary, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"summary-Good\">9<", summary, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"summary-Forgot\">1<", summary, StringComparison.Ordinal);
        await using var check = database.CreateContext();
        var attemptId = long.Parse(set.Split('/')[^1], CultureInfo.InvariantCulture);
        var attempt = await check.Attempts.Include(a => a.Items).SingleAsync(a => a.Id == attemptId);
        Assert.Equal((AttemptMode.Flashcard, AttemptSource.Review, AttemptStatus.Submitted, 10), (attempt.Mode, attempt.Source, attempt.Status, attempt.Items.Count));
        Assert.Equal(questions[11], attempt.Items.Single(i => i.Position == 1).QuestionId);
        var cards = await check.ReviewCards.Include(c => c.Logs).Where(c => c.UserId == user.Id).ToListAsync();
        Assert.Equal(10, cards.Count);
        Assert.All(cards, c => Assert.Single(c.Logs));
        var due = cards.Single(c => c.QuestionId == questions[11]);
        Assert.Equal((ReviewCardState.Learning, 1), (due.State, due.Lapses));
    }

    [Fact]
    public async Task Questions_from_exams_without_full_access_are_excluded()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long paidExamId;
        long freeQuestion;
        List<long> paidQuestions;
        await using (var db = database.CreateContext())
        {
            var paid = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(3, trial: 3).Build());
            var free = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
            paidExamId = paid.Id;
            paidQuestions = [.. paid.Questions.Select(q => q.QuestionId)];
            freeQuestion = free.Questions.Single().QuestionId;
            db.ExamPurchases.Add(new ExamPurchase
            {
                UserId = user.Id, ExamId = paid.Id, PricePaidIdr = 49_000, Status = ExamPurchaseStatus.Expired,
                PurchasedAt = app.Clock.UtcNow.AddDays(-40), AccessExpiresAt = app.Clock.UtcNow.AddDays(-10),
            });
            foreach (var questionId in paidQuestions.Append(freeQuestion))
            {
                db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = questionId, State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-1) });
            }

            await db.SaveChangesAsync();
        }

        var decks = await learner.GetStringAsync(new Uri("/learn/kartu", UriKind.Relative));
        var set = await StartAsync(learner, null);
        using var blocked = await learner.PostFormAsync("/learn/kartu", new Dictionary<string, string> { ["examId"] = paidExamId.ToString(CultureInfo.InvariantCulture) }, "/learn/kartu?handler=Start");

        Assert.DoesNotContain($"data-exam=\"{paidExamId}\"", decks, StringComparison.Ordinal);
        Assert.Equal("/learn/kartu", blocked.Headers.Location!.OriginalString);
        await using var check = database.CreateContext();
        var attemptId = long.Parse(set.Split('/')[^1], CultureInfo.InvariantCulture);
        var items = await check.AttemptItems.Where(i => i.AttemptId == attemptId).Select(i => i.QuestionId).ToListAsync();
        Assert.Equal([freeQuestion], items);
    }

    private static async Task<string> StartAsync(HttpClient client, string? examId)
    {
        var fields = examId is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["examId"] = examId };
        using var response = await client.PostFormAsync("/learn/kartu", fields, "/learn/kartu?handler=Start");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Matches("^/learn/kartu/\\d+$", location);
        return location;
    }

    [GeneratedRegex("data-testid=\"flashcard\" data-position=\"(\\d+)\"")]
    private static partial Regex PositionPattern();
}
