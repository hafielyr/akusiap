using System.Globalization;
using System.Net;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Own;

/// <summary>OWN-06: a private CustomMix set of own questions plus questions from exams with full access only.</summary>
[Collection(UsesDatabase.Name)]
public sealed class MixedPracticeTests(PostgresFixture database)
{
    [Fact]
    public async Task App_questions_without_full_access_are_excluded_and_the_set_has_no_exam()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long bought, notBought;
        List<long> boughtQuestions, ownQuestions;
        await using (var db = database.CreateContext())
        {
            var a = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(3).Build());
            var b = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(3, trial: 3).Build());
            (bought, notBought) = (a.Id, b.Id);
            boughtQuestions = [.. a.Questions.Select(q => q.QuestionId)];
            db.ExamPurchases.Add(new ExamPurchase
            {
                UserId = user.Id, ExamId = a.Id, PricePaidIdr = 49_000, Status = ExamPurchaseStatus.Active,
                PurchasedAt = app.Clock.UtcNow.AddDays(-1), AccessExpiresAt = app.Clock.UtcNow.AddDays(30),
            });
            var own = await db.SaveAsync(TestData.Exam().OwnedBy(user).WithQuestions(2).Build());
            ownQuestions = [.. own.Questions.Select(q => q.QuestionId)];
            await db.SaveChangesAsync();
        }

        var page = await learner.GetStringAsync(new Uri("/learn/campuran", UriKind.Relative));
        using var start = await PostAsync(learner, includeOwn: true, examIds: [bought, notBought], topicKeys: []);

        Assert.Contains($"data-exam=\"{bought}\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain($"data-exam=\"{notBought}\"", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var attempt = await LoadAsync(start.Headers.Location!.OriginalString);
        Assert.Equal((AttemptSource.CustomMix, (long?)null, AttemptMode.Practice), (attempt.Source, attempt.ExamId, attempt.Mode));
        Assert.Equal(boughtQuestions.Concat(ownQuestions).Order(), attempt.Items.Select(i => i.QuestionId).Order());
    }

    [Fact]
    public async Task Single_topics_can_be_picked_and_nothing_selected_is_refused()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        long examId, topicId;
        List<long> topicQuestions;
        await using (var db = database.CreateContext())
        {
            var exam = TestData.Exam().Free().WithQuestions(4).Build();
            var other = new Topic { Subject = exam.Subject, Name = "Topik lain " + TestData.Unique() };
            var moved = exam.Questions.Where(q => q.SortOrder > 2).Select(q => q.Question!).ToList();
            moved.ForEach(q => q.Topic = other);
            examId = (await db.SaveAsync(exam)).Id;
            topicId = other.Id;
            topicQuestions = [.. moved.Select(q => q.Id)];
        }

        using var empty = await PostAsync(learner, includeOwn: false, examIds: [], topicKeys: []);
        using var start = await PostAsync(learner, includeOwn: false, examIds: [], topicKeys: [$"{examId}:{topicId}"]);

        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains("Pilih minimal satu sumber soal.", await empty.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var attempt = await LoadAsync(start.Headers.Location!.OriginalString);
        Assert.Equal(topicQuestions.Order(), attempt.Items.Select(i => i.QuestionId).Order());
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, bool includeOwn, long[] examIds, string[] topicKeys)
    {
        var token = await client.GetAntiforgeryTokenAsync("/learn/campuran");
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("IncludeOwn", includeOwn ? "true" : "false"),
            new("QuestionCount", "50"),
        };
        fields.AddRange(examIds.Select(id => new KeyValuePair<string, string>("ExamIds", id.ToString(CultureInfo.InvariantCulture))));
        fields.AddRange(topicKeys.Select(k => new KeyValuePair<string, string>("TopicKeys", k)));
        using var content = new FormUrlEncodedContent(fields);
        return await client.PostAsync(new Uri("/learn/campuran", UriKind.Relative), content);
    }

    private async Task<Attempt> LoadAsync(string playerUrl)
    {
        var id = long.Parse(playerUrl.Split('/')[^1], CultureInfo.InvariantCulture);
        await using var db = database.CreateContext();
        return await db.Attempts.Include(a => a.Items).SingleAsync(a => a.Id == id);
    }
}
