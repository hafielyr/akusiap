using System.Globalization;
using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Memorization;

/// <summary>SRS-01: the learner's own questions get cards and show up in "Kartu saya" next to app questions.</summary>
[Collection(UsesDatabase.Name)]
public sealed class OwnQuestionCardsTests(PostgresFixture database)
{
    [Fact]
    public async Task Own_questions_appear_in_sets_alongside_app_questions()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long appQuestion, ownQuestion, retiredOwn;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(1).Build());
            appQuestion = exam.Questions.Single().QuestionId;
            var topic = new Topic { Name = "Topik " + TestData.Unique(), Subject = new Subject { Name = "Subjek " + TestData.Unique() } };
            var own = Own(user.Id, topic, QuestionStatus.Active);
            var retired = Own(user.Id, topic, QuestionStatus.Retired);
            db.Questions.AddRange(own, retired);
            await db.SaveChangesAsync();
            (ownQuestion, retiredOwn) = (own.Id, retired.Id);
            foreach (var id in new[] { appQuestion, ownQuestion, retiredOwn })
            {
                db.ReviewCards.Add(new ReviewCard { UserId = user.Id, QuestionId = id, State = ReviewCardState.Learning, DueAt = app.Clock.UtcNow.AddHours(-1) });
            }

            await db.SaveChangesAsync();
        }

        var decks = await learner.GetStringAsync(new Uri("/learn/kartu", UriKind.Relative));
        using var start = await learner.PostFormAsync("/learn/kartu", new Dictionary<string, string>(), "/learn/kartu?handler=Start");

        Assert.Contains("2 kartu jatuh tempo", decks, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var attemptId = long.Parse(start.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);
        await using var check = database.CreateContext();
        var items = await check.AttemptItems.Where(i => i.AttemptId == attemptId).Select(i => i.QuestionId).ToListAsync();
        Assert.Equal(new[] { appQuestion, ownQuestion }.Order(), items.Order());
    }

    private static Question Own(Guid ownerId, Topic topic, QuestionStatus status)
    {
        var q = new Question { OwnerUserId = ownerId, Topic = topic, QuestionType = QuestionType.TrueFalse, Text = "Soal sendiri?", Explanation = "Ok.", Status = status, CreatedAt = TestData.Now, UpdatedAt = TestData.Now };
        q.Options.Add(new QuestionOption { Text = "Benar", IsCorrect = true, SortOrder = 1 });
        q.Options.Add(new QuestionOption { Text = "Salah", SortOrder = 2 });
        return q;
    }
}
