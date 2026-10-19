using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;

namespace Akusiap.IntegrationTests.Own;

/// <summary>OWN-05: the usage bar shows the numbers from AllowancePolicy on own-content pages and the profile.</summary>
[Collection(UsesDatabase.Name)]
public sealed class AllowanceWidgetTests(PostgresFixture database)
{
    [Fact]
    public async Task The_bar_shows_used_and_allowed_questions_and_exams()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        await using (var db = database.CreateContext())
        {
            var topic = new Topic { Name = "Topik " + TestData.Unique(), Subject = new Subject { Name = "Subjek " + TestData.Unique() } };
            for (var i = 0; i < 23; i++)
            {
                var q = new Question { OwnerUserId = user.Id, Topic = topic, QuestionType = QuestionType.TrueFalse, Text = $"Q{i}?", Explanation = "Ok.", Status = QuestionStatus.Active, CreatedAt = TestData.Now, UpdatedAt = TestData.Now };
                q.Options.Add(new QuestionOption { Text = "Benar", IsCorrect = true, SortOrder = 1 });
                q.Options.Add(new QuestionOption { Text = "Salah", SortOrder = 2 });
                db.Questions.Add(q);
            }

            await db.SaveChangesAsync();
            await db.SaveAsync(TestData.Exam().OwnedBy(user).Build());
        }

        var ownPage = await learner.GetStringAsync(new Uri("/learn/soal-saya", UriKind.Relative));
        var profile = await learner.GetStringAsync(new Uri("/learn/profil", UriKind.Relative));

        foreach (var html in new[] { ownPage, profile })
        {
            Assert.Contains("data-testid=\"allowance-questions\">23/50<", html, StringComparison.Ordinal);
            Assert.Contains("data-testid=\"allowance-exams\">1/1<", html, StringComparison.Ordinal);
            Assert.Contains("href=\"/harga\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Admins_see_unlimited()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);

        var html = await admin.GetStringAsync(new Uri("/learn/soal-saya", UriKind.Relative));

        Assert.Contains("data-testid=\"allowance-unlimited\"", html, StringComparison.Ordinal);
    }
}
