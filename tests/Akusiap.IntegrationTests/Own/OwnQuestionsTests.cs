using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Own;

/// <summary>OWN-03: learners write their own questions within their allowance (Free plan: 50).</summary>
[Collection(UsesDatabase.Name)]
public sealed class OwnQuestionsTests(PostgresFixture database)
{
    [Fact]
    public async Task A_free_user_adds_questions_and_the_51st_is_rejected_with_an_upgrade_link()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var topicId = await TopicAsync();

        using var first = await learner.PostFormAsync("/learn/soal-saya/baru", Form(topicId, "Soal pertama saya?"));
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        await AddOwnQuestionsAsync(user.Id, topicId, 49);

        var list = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri("/learn/soal-saya", UriKind.Relative)));
        using var fiftyFirst = await learner.PostFormAsync("/learn/soal-saya/baru", Form(topicId, "Soal ke-51?"));
        var html = WebUtility.HtmlDecode(await fiftyFirst.Content.ReadAsStringAsync());

        Assert.Contains("Soal pertama saya?", list, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"add-question\"", list, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, fiftyFirst.StatusCode);
        Assert.Contains("Anda sudah memakai semua jatah 50 soal.", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/harga\"", html, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.Equal(50, await db.Questions.CountAsync(q => q.OwnerUserId == user.Id));
        var mine = await db.Questions.SingleAsync(q => q.OwnerUserId == user.Id && q.Text == "Soal pertama saya?");
        Assert.Equal(QuestionStatus.Active, mine.Status);
    }

    [Fact]
    public async Task Over_the_cap_questions_are_read_only()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var topicId = await TopicAsync();
        var ids = await AddOwnQuestionsAsync(user.Id, topicId, 55);

        var page = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri("/learn/soal-saya", UriKind.Relative)));
        using var edit = await learner.PostFormAsync($"/learn/soal-saya/{ids[0]}", Form(topicId, "Diubah?"));

        Assert.Contains("data-testid=\"allowance-readonly\"", page, StringComparison.Ordinal);
        Assert.Contains("hanya bisa dilihat dan dilatih", WebUtility.HtmlDecode(await edit.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.NotEqual("Diubah?", (await db.Questions.SingleAsync(q => q.Id == ids[0])).Text);
    }

    [Fact]
    public async Task Delete_is_refused_for_questions_used_in_an_attempt_and_others_get_404()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var topicId = await TopicAsync();
        var ids = await AddOwnQuestionsAsync(user.Id, topicId, 2);
        await using (var db = database.CreateContext())
        {
            var attempt = new Attempt { UserId = user.Id, Source = AttemptSource.CustomMix, Mode = AttemptMode.Practice, RulesSnapshot = "{}", StartedAt = TestData.Now, LastSavedAt = TestData.Now };
            attempt.Items.Add(new AttemptItem { QuestionId = ids[1], Position = 1 });
            db.Attempts.Add(attempt);
            await db.SaveChangesAsync();
        }

        using var deleted = await learner.PostFormAsync($"/learn/soal-saya/{ids[0]}", new Dictionary<string, string>(), $"/learn/soal-saya/{ids[0]}?handler=Delete");
        using var refused = await learner.PostFormAsync($"/learn/soal-saya/{ids[1]}", new Dictionary<string, string>(), $"/learn/soal-saya/{ids[1]}?handler=Delete");
        var refusedPage = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"/learn/soal-saya/{ids[1]}", UriKind.Relative)));
        using var notMine = await other.GetAsync(new Uri($"/learn/soal-saya/{ids[1]}", UriKind.Relative));

        Assert.Contains("tidak bisa dihapus", refusedPage, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, notMine.StatusCode);
        await using var check = database.CreateContext();
        Assert.False(await check.Questions.AnyAsync(q => q.Id == ids[0]));
        Assert.True(await check.Questions.AnyAsync(q => q.Id == ids[1]));
    }

    // ---------- Helpers ----------

    private static Dictionary<string, string> Form(long topicId, string text) => new()
    {
        ["Input.TopicId"] = topicId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["Input.QuestionType"] = "SingleChoice",
        ["Input.Text"] = text,
        ["Input.Explanation"] = "Karena.",
        ["Input.Status"] = "Active",
        ["Input.Options[0].Text"] = "Ya",
        ["Input.Options[0].IsCorrect"] = "true",
        ["Input.Options[1].Text"] = "Tidak",
        ["Input.Options[1].IsCorrect"] = "false",
    };

    private async Task<long> TopicAsync()
    {
        await using var db = database.CreateContext();
        var topic = new Topic { Name = "Topik " + TestData.Unique(), Subject = new Subject { Name = "Subjek " + TestData.Unique() } };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.Id;
    }

    private async Task<List<long>> AddOwnQuestionsAsync(Guid userId, long topicId, int count)
    {
        await using var db = database.CreateContext();
        var questions = Enumerable.Range(1, count).Select(i =>
        {
            var q = new Question
            {
                OwnerUserId = userId, TopicId = topicId, QuestionType = QuestionType.TrueFalse, Text = $"Soal sendiri {i}?", Explanation = "Ok.",
                Status = QuestionStatus.Active, CreatedAt = TestData.Now, UpdatedAt = TestData.Now,
            };
            q.Options.Add(new QuestionOption { Text = "Benar", IsCorrect = true, SortOrder = 1 });
            q.Options.Add(new QuestionOption { Text = "Salah", SortOrder = 2 });
            return q;
        }).ToList();
        db.Questions.AddRange(questions);
        await db.SaveChangesAsync();
        return [.. questions.Select(q => q.Id)];
    }
}
