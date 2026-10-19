using System.Globalization;
using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Own;

/// <summary>OWN-04: learners build exams from their own questions, within the allowance, and can practise them.</summary>
[Collection(UsesDatabase.Name)]
public sealed class OwnExamsTests(PostgresFixture database)
{
    private const string CreateUrl = "/learn/ujian-saya/baru";

    [Fact]
    public async Task The_free_users_second_exam_is_rejected()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var questions = await SeedQuestionsAsync(user.Id, 2);

        using var first = await PostAsync(learner, CreateUrl, "Ujian pertama", questions);
        using var second = await PostAsync(learner, CreateUrl, "Ujian kedua", questions);
        var html = WebUtility.HtmlDecode(await second.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("jatah 1 ujian", html, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        var exams = await db.Exams.Where(e => e.OwnerUserId == user.Id).Include(e => e.RulePreset).Include(e => e.Questions).ToListAsync();
        var exam = Assert.Single(exams);
        Assert.Equal(("Ujian pertama", OwnerKind.Learner, ExamStatus.Draft, true), (exam.Title, exam.OwnerKind, exam.Status, exam.IsFree));
        Assert.Equal(user.Id, exam.RulePreset!.OwnerUserId);
        Assert.Equal(questions, exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId));
    }

    [Fact]
    public async Task Only_own_active_questions_can_be_used()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        var other = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(other);
        }

        var mine = await SeedQuestionsAsync(user.Id, 1);
        var theirs = await SeedQuestionsAsync(other.Id, 1);

        using var foreign = await PostAsync(learner, CreateUrl, "Campuran", [.. mine, .. theirs]);
        using var empty = await PostAsync(learner, CreateUrl, "Kosong", []);

        Assert.Equal((HttpStatusCode.OK, HttpStatusCode.OK), (foreign.StatusCode, empty.StatusCode));
        Assert.Contains("data-testid=\"error-questions\"", await foreign.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var check = database.CreateContext();
        Assert.False(await check.Exams.AnyAsync(e => e.OwnerUserId == user.Id));
    }

    [Fact]
    public async Task The_owner_can_start_practice_on_their_own_exam_and_others_cannot()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        using var stranger = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var questions = await SeedQuestionsAsync(user.Id, 3);
        using var created = await PostAsync(learner, CreateUrl, "Latihan pribadi", questions, published: true);
        var examId = long.Parse(created.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);

        var list = await learner.GetStringAsync(new Uri("/learn/ujian-saya", UriKind.Relative));
        using var start = await learner.PostFormAsync($"/ujian/{examId}/mulai", new Dictionary<string, string>
        {
            ["Form.Mode"] = "Practice", ["Form.Untimed"] = "true", ["Form.ShuffleQuestions"] = "false", ["Form.ShuffleOptions"] = "false",
        });
        using var strangerSetup = await stranger.GetAsync(new Uri($"/ujian/{examId}/mulai", UriKind.Relative));

        Assert.Contains($"href=\"/ujian/{examId}/mulai\"", list, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.StartsWith("/sesi/", start.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.NotEqual(HttpStatusCode.OK, strangerSetup.StatusCode);
        await using var db = database.CreateContext();
        var attempt = await db.Attempts.Include(a => a.Items).SingleAsync(a => a.ExamId == examId);
        Assert.Equal((user.Id, AttemptMode.Practice), (attempt.UserId!.Value, attempt.Mode));
        Assert.Equal(questions.Order(), attempt.Items.Select(i => i.QuestionId).Order());
        Assert.Equal(ExamStatus.Published, (await db.Exams.SingleAsync(e => e.Id == examId)).Status);
    }

    private async Task<List<long>> SeedQuestionsAsync(Guid ownerId, int count)
    {
        await using var db = database.CreateContext();
        var topic = new Topic { Name = "Topik " + TestData.Unique(), Subject = new Subject { Name = "Subjek " + TestData.Unique() } };
        var questions = new List<Question>();
        for (var i = 0; i < count; i++)
        {
            var q = new Question { OwnerUserId = ownerId, Topic = topic, QuestionType = QuestionType.TrueFalse, Text = $"Soal sendiri {i}?", Explanation = "Ok.", Status = QuestionStatus.Active, CreatedAt = TestData.Now, UpdatedAt = TestData.Now };
            q.Options.Add(new QuestionOption { Text = "Benar", IsCorrect = true, SortOrder = 1 });
            q.Options.Add(new QuestionOption { Text = "Salah", SortOrder = 2 });
            db.Questions.Add(q);
            questions.Add(q);
        }

        await db.SaveChangesAsync();
        return [.. questions.Select(q => q.Id)];
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string title, IEnumerable<long> questionIds, bool published = false)
    {
        var token = await client.GetAntiforgeryTokenAsync(url);
        var fields = new List<KeyValuePair<string, string>>
        {
            new("__RequestVerificationToken", token),
            new("Input.Title", title),
            new("Input.Description", "Untuk latihan sendiri."),
            new("Input.HintsEnabled", "true"),
            new("Input.ShowAnswers", nameof(ShowAnswers.AfterSubmit)),
            new("Input.Published", published ? "true" : "false"),
        };
        fields.AddRange(questionIds.Select(id => new KeyValuePair<string, string>("Input.QuestionIds", id.ToString(CultureInfo.InvariantCulture))));
        using var content = new FormUrlEncodedContent(fields);
        return await client.PostAsync(new Uri(url, UriKind.Relative), content);
    }
}
