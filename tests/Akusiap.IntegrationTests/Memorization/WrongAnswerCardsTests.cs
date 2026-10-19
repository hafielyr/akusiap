using Akusiap.Application.Abstractions;
using Akusiap.Application.Sessions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Sessions;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Memorization;

/// <summary>MEM-01: finishing an attempt turns each wrong question into a review card due now, once per user and question.</summary>
[Collection(UsesDatabase.Name)]
public sealed class WrongAnswerCardsTests(PostgresFixture database)
{
    [Fact]
    public async Task Wrong_answers_upsert_learning_cards_and_correct_answers_do_not()
    {
        await using var app = new AppFactory(database);
        var user = TestData.User().Build();
        Exam exam;
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(user);
            exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(3).Build());
        }

        var questions = exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.Question!).ToList();
        await using (var db = database.CreateContext())
        {
            db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = questions[2].Id, State = ReviewCardState.Memorized, DueAt = TestData.Now.AddDays(30), IntervalDays = 30, Repetitions = 5 });
            await db.SaveChangesAsync();
        }

        // Question 1 right, question 2 wrong, question 3 unanswered (wrong).
        var first = await SeedAttemptAsync(user.Id, exam.Id, questions, [Correct(questions[0]), Wrong(questions[1]), null]);
        await FinishAsync(app, first);
        await FinishAsync(app, first);
        var second = await SeedAttemptAsync(user.Id, exam.Id, questions, [Correct(questions[0]), Wrong(questions[1]), Correct(questions[2])]);
        await FinishAsync(app, second);

        await using var check = database.CreateContext();
        var cards = await check.ReviewCards.Where(c => c.UserId == user.Id).OrderBy(c => c.QuestionId).ToListAsync();
        Assert.Equal([questions[1].Id, questions[2].Id], cards.Select(c => c.QuestionId));
        Assert.All(cards, c => Assert.Equal(ReviewCardState.Learning, c.State));
        Assert.All(cards, c => Assert.Equal(app.Clock.UtcNow, c.DueAt));
        Assert.Equal(5, cards[1].Repetitions);
    }

    [Fact]
    public async Task Guest_attempts_create_no_cards()
    {
        await using var app = new AppFactory(database);
        Exam exam;
        await using (var db = database.CreateContext())
        {
            exam = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(2, trial: 2).Build());
        }

        var questions = exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.Question!).ToList();
        var attemptId = await SeedAttemptAsync(null, exam.Id, questions, [Wrong(questions[0]), null], AttemptSource.Trial);
        await FinishAsync(app, attemptId);

        await using var check = database.CreateContext();
        var ids = questions.Select(q => q.Id).ToList();
        Assert.False(await check.ReviewCards.AnyAsync(c => ids.Contains(c.QuestionId)));
    }

    private static long Correct(Question question) => question.Options.Single(o => o.IsCorrect).Id;

    private static long Wrong(Question question) => question.Options.First(o => !o.IsCorrect).Id;

    private async Task<long> SeedAttemptAsync(Guid? userId, long examId, List<Question> questions, long?[] answers, AttemptSource source = AttemptSource.Exam)
    {
        await using var db = database.CreateContext();
        var attempt = new Attempt
        {
            UserId = userId,
            GuestToken = userId is null ? Guid.NewGuid() : null,
            ExamId = examId,
            Source = source,
            Mode = AttemptMode.Practice,
            RulesSnapshot = "{}",
            StartedAt = TestData.Now,
            LastSavedAt = TestData.Now,
        };
        for (var i = 0; i < questions.Count; i++)
        {
            var item = new AttemptItem { QuestionId = questions[i].Id, Position = i + 1, IsAnswered = answers[i] is not null };
            if (answers[i] is { } option)
            {
                item.SelectedOptions.Add(new AttemptItemOption { QuestionOptionId = option });
            }

            attempt.Items.Add(item);
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }

    private static async Task FinishAsync(AppFactory app, long attemptId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var attempt = await db.Attempts.SingleAsync(a => a.Id == attemptId);
        await scope.ServiceProvider.GetRequiredService<AttemptFinisher>().FinishAsync(attempt, AttemptStatus.Submitted, app.Clock.UtcNow, CancellationToken.None);
    }
}
