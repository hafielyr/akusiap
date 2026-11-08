using System.Globalization;
using System.Net;
using Akusiap.Domain;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Planning;

/// <summary>PLN-03 (D-123): a card for an exam with a goal is due no later than the day before the exam.</summary>
[Collection(UsesDatabase.Name)]
public sealed class ExamDayCapTests(PostgresFixture database)
{
    [Fact]
    public async Task A_card_whose_interval_passes_the_exam_is_due_the_day_before_it()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var (examId, questionId) = await SaveExamWithCardAsync(user.Id, app.Clock.UtcNow);
        var examDate = JakartaTime.Today(app.Clock.UtcNow).AddDays(5);
        await SetGoalAsync(learner, examId, examDate);

        var card = await RateGoodAsync(learner, user.Id, examId, questionId);

        // SM-2 alone would give 38 days (15 × 2.5); the goal caps the due date, not the interval.
        Assert.Equal(JakartaTime.StartOfDayUtc(examDate.AddDays(-1)), card.DueAt);
        Assert.Equal((38m, ReviewCardState.Memorized), (card.IntervalDays, card.State));
    }

    [Fact]
    public async Task Cards_without_a_goal_and_reviews_on_the_last_day_follow_sm2()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var (noGoalExam, noGoalQuestion) = await SaveExamWithCardAsync(user.Id, app.Clock.UtcNow);
        var (lastDayExam, lastDayQuestion) = await SaveExamWithCardAsync(user.Id, app.Clock.UtcNow);
        await SetGoalAsync(learner, lastDayExam, JakartaTime.Today(app.Clock.UtcNow).AddDays(5));

        var noGoal = await RateGoodAsync(learner, user.Id, noGoalExam, noGoalQuestion);
        app.Clock.Advance(TimeSpan.FromDays(4)); // the day before the exam: the cap has passed
        var lastDay = await RateGoodAsync(learner, user.Id, lastDayExam, lastDayQuestion);

        Assert.Equal(app.Clock.UtcNow.AddDays(-4).AddDays(38), noGoal.DueAt);
        Assert.Equal(app.Clock.UtcNow.AddDays(38), lastDay.DueAt);
    }

    /// <summary>A free one-question exam and a due card on it, 15 days into SM-2 (so Good gives 38 days).</summary>
    private async Task<(long ExamId, long QuestionId)> SaveExamWithCardAsync(Guid userId, DateTime now)
    {
        await using var db = database.CreateContext();
        var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
        var questionId = exam.Questions.Single().QuestionId;
        db.ReviewCards.Add(new ReviewCard { UserId = userId, QuestionId = questionId, State = ReviewCardState.Review, IntervalDays = 15m, EaseFactor = 2.5m, Repetitions = 3, DueAt = now.AddHours(-1) });
        await db.SaveChangesAsync();
        return (exam.Id, questionId);
    }

    private async Task<ReviewCard> RateGoodAsync(HttpClient client, Guid userId, long examId, long questionId)
    {
        using var started = await client.PostFormAsync("/learn/kartu", new Dictionary<string, string> { ["examId"] = examId.ToString(CultureInfo.InvariantCulture) }, "/learn/kartu?handler=Start");
        var set = started.Headers.Location!.OriginalString;
        Assert.NotEqual("/learn/kartu", set);
        using var rated = await client.PostFormAsync(set, new Dictionary<string, string> { ["position"] = "1", ["rating"] = nameof(ReviewRating.Good) }, $"{set}?handler=Rate");
        Assert.True(rated.StatusCode is HttpStatusCode.OK or HttpStatusCode.Redirect, $"Rate answered {rated.StatusCode}");
        await using var db = database.CreateContext();
        return await db.ReviewCards.AsNoTracking().SingleAsync(c => c.UserId == userId && c.QuestionId == questionId);
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
