using System.Globalization;
using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>ADM-T4: add, remove, reorder and trial-toggle exam questions through the htmx handlers, with counters.</summary>
[Collection(UsesDatabase.Name)]
public sealed class ExamQuestionPickerTests(PostgresFixture database)
{
    [Fact]
    public async Task Adding_a_question_returns_only_the_refreshed_panel_with_updated_counters()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (examId, topicId) = await CreateExamAndTopicAsync();
        var questionId = await CreateQuestionAsync(topicId, "Soal baru untuk ujian");

        using var response = await PostAsync(admin, examId, "AddQuestion", ("questionId", questionId));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("<html", html, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("<div id=\"exam-questions-panel\"", html.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("data-testid=\"count-total\">1 soal<", html, StringComparison.Ordinal);
        Assert.Contains("Soal baru untuk ujian", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"search-row-{questionId}\" hx-swap-oob=\"true\"", html, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        var link = await db.ExamQuestions.SingleAsync(eq => eq.ExamId == examId);
        Assert.Equal((questionId, 1, false), (link.QuestionId, link.SortOrder, link.IsTrial));
    }

    [Fact]
    public async Task Adding_the_same_question_twice_is_prevented()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (examId, topicId) = await CreateExamAndTopicAsync();
        var questionId = await CreateQuestionAsync(topicId, "Soal kembar");

        using var first = await PostAsync(admin, examId, "AddQuestion", ("questionId", questionId));
        using var second = await PostAsync(admin, examId, "AddQuestion", ("questionId", questionId));
        var html = await second.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("Soal ini sudah ada di ujian.", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"count-total\">1 soal<", html, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.ExamQuestions.CountAsync(eq => eq.ExamId == examId));
    }

    [Theory]
    [InlineData(QuestionStatus.Draft)]
    [InlineData(QuestionStatus.Retired)]
    public async Task Only_active_questions_can_be_added(QuestionStatus status)
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (examId, topicId) = await CreateExamAndTopicAsync();
        var questionId = await CreateQuestionAsync(topicId, "Soal tidak aktif", status);

        using var response = await PostAsync(admin, examId, "AddQuestion", ("questionId", questionId));

        Assert.Contains("Hanya soal aktif dari bank soal yang bisa ditambahkan.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.False(await db.ExamQuestions.AnyAsync(eq => eq.ExamId == examId));
    }

    [Fact]
    public async Task Questions_can_be_reordered_toggled_as_trial_and_removed_and_positions_stay_1_to_n()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (examId, topicId) = await CreateExamAndTopicAsync();
        var a = await CreateQuestionAsync(topicId, "Soal A");
        var b = await CreateQuestionAsync(topicId, "Soal B");
        var c = await CreateQuestionAsync(topicId, "Soal C");
        foreach (var id in new[] { a, b, c })
        {
            using var _ = await PostAsync(admin, examId, "AddQuestion", ("questionId", id));
        }

        using (await PostAsync(admin, examId, "MoveQuestion", ("questionId", c), ("step", -1))) { }
        using (await PostAsync(admin, examId, "MoveQuestion", ("questionId", a), ("step", -1))) { }
        Assert.Equal(new[] { a, c, b }, await OrderAsync(examId));

        using var trial = await PostAsync(admin, examId, "Trial", ("questionId", c), ("isTrial", "true"));
        Assert.Contains("data-testid=\"count-trial\">1 soal coba<", await trial.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var removed = await PostAsync(admin, examId, "RemoveQuestion", ("questionId", a));
        var html = await removed.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"count-total\">2 soal<", html, StringComparison.Ordinal);
        Assert.Equal(new[] { c, b }, await OrderAsync(examId));
        await using var db = database.CreateContext();
        var links = await db.ExamQuestions.Where(eq => eq.ExamId == examId).OrderBy(eq => eq.SortOrder).ToListAsync();
        Assert.Equal(new[] { (1, true), (2, false) }, links.Select(l => (l.SortOrder, l.IsTrial)).ToArray());

        using var untrial = await PostAsync(admin, examId, "Trial", ("questionId", c));
        Assert.Contains("data-testid=\"count-trial\">0 soal coba<", await untrial.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Counters_show_per_topic_totals_and_the_trial_requirement_for_paid_exams()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync<Exam>(TestData.Exam().Draft().Paid(49_000).WithQuestions(35, trial: 12))).Id;
        }

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/ujian/{examId}", UriKind.Relative)));

        Assert.Contains("35 soal", page, StringComparison.Ordinal);
        Assert.Contains("12 soal coba", page, StringComparison.Ordinal);
        Assert.Contains("Untuk terbit, ujian berbayar perlu 20–35 soal coba.", page, StringComparison.Ordinal);
        Assert.Contains("35 soal, 12 coba", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Search_returns_active_matches_and_marks_ones_already_added()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (examId, topicId) = await CreateExamAndTopicAsync();
        var token = TestData.Unique();
        var added = await CreateQuestionAsync(topicId, $"Cari {token} satu");
        var notAdded = await CreateQuestionAsync(topicId, $"Cari {token} dua");
        var retired = await CreateQuestionAsync(topicId, $"Cari {token} pensiun", QuestionStatus.Retired);
        using (await PostAsync(admin, examId, "AddQuestion", ("questionId", added))) { }

        var html = await admin.GetStringAsync(new Uri($"/admin/ujian/{examId}?handler=Search&q={token}&topik={topicId}", UriKind.Relative));

        Assert.StartsWith("<div id=\"question-search-results\"", html.TrimStart(), StringComparison.Ordinal);
        Assert.Contains($"id=\"search-row-{added}\"", html, StringComparison.Ordinal);
        Assert.Contains($"id=\"search-row-{notAdded}\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain($"id=\"search-row-{retired}\"", html, StringComparison.Ordinal);
        Assert.Equal(1, html.Split("data-testid=\"already-added\"").Length - 1);
    }

    [Fact]
    public async Task Picker_needs_an_antiforgery_token_and_an_admin()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, topicId) = await CreateExamAndTopicAsync();
        var questionId = await CreateQuestionAsync(topicId, "Soal dijaga");
        using var noToken = new FormUrlEncodedContent(new Dictionary<string, string> { ["questionId"] = $"{questionId}" });

        using var withoutToken = await admin.PostAsync(new Uri($"/admin/ujian/{examId}?handler=AddQuestion", UriKind.Relative), noToken);
        using var asLearner = await learner.GetAsync(new Uri($"/admin/ujian/{examId}?handler=Search", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, asLearner.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private async Task<(long ExamId, long TopicId)> CreateExamAndTopicAsync()
    {
        await using var db = database.CreateContext();
        var exam = await db.SaveAsync<Exam>(TestData.Exam().Draft().Paid(49_000));
        var topic = new Topic { Subject = new Subject { Name = $"Subjek {TestData.Unique()}" }, Name = "Topik pilih" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return (exam.Id, topic.Id);
    }

    private async Task<long> CreateQuestionAsync(long topicId, string text, QuestionStatus status = QuestionStatus.Active)
    {
        await using var db = database.CreateContext();
        var question = new Question
        {
            TopicId = topicId,
            QuestionType = QuestionType.TrueFalse,
            Text = text,
            Explanation = "Pembahasan.",
            Status = status,
            CreatedAt = TestData.Now,
            UpdatedAt = TestData.Now,
        };
        question.Options.Add(new QuestionOption { Text = "Benar", IsCorrect = true, SortOrder = 1 });
        question.Options.Add(new QuestionOption { Text = "Salah", SortOrder = 2 });
        db.Questions.Add(question);
        await db.SaveChangesAsync();
        return question.Id;
    }

    private async Task<long[]> OrderAsync(long examId)
    {
        await using var db = database.CreateContext();
        return await db.ExamQuestions.Where(eq => eq.ExamId == examId).OrderBy(eq => eq.SortOrder).Select(eq => eq.QuestionId).ToArrayAsync();
    }

    /// <summary>Posts to a picker handler like htmx does (form fields plus the antiforgery token from the exam page).</summary>
    private static Task<HttpResponseMessage> PostAsync(HttpClient admin, long examId, string handler, params (string Name, object Value)[] fields) =>
        admin.PostFormAsync(
            $"/admin/ujian/{examId}",
            fields.ToDictionary(f => f.Name, f => Convert.ToString(f.Value, CultureInfo.InvariantCulture)!),
            postUrl: $"/admin/ujian/{examId}?handler={handler}");
}
