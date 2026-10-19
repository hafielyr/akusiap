using System.Globalization;
using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>ADM-T2: question list, create and edit, validation messages and retiring.</summary>
[Collection(UsesDatabase.Name)]
public sealed class QuestionEditorTests(PostgresFixture database)
{
    [Fact]
    public async Task Admin_creates_a_question_with_every_field()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var topicId = await CreateTopicAsync();

        using var response = await admin.PostFormAsync("/admin/soal/baru", Form(topicId, QuestionType.SingleChoice, "Ibu kota Indonesia adalah …",
            [(null, "Jakarta", true), (null, "Bandung", false), (null, "Surabaya", false)]).WithKeyFact("Ibu kota: Jakarta"));
        var id = IdFrom(response);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Perubahan disimpan.", await admin.GetStringAsync(response.Headers.Location), StringComparison.Ordinal);
        await using var db = database.CreateContext();
        var question = await db.Questions.Include(q => q.Options).SingleAsync(q => q.Id == id);
        Assert.Equal((topicId, QuestionType.SingleChoice, QuestionStatus.Active), (question.TopicId, question.QuestionType, question.Status));
        Assert.Equal(("Ibu kota Indonesia adalah …", "Jakarta, sejak 1961.", "Ibu kota: Jakarta"), (question.Text, question.Explanation, question.KeyFact));
        Assert.Equal(("Kalikan saja.", (short?)2, "Kisi-kisi TWK 2026"), (question.Hint, question.Difficulty, question.SourceNote));
        Assert.Null(question.OwnerUserId);
        Assert.Equal(app.Clock.UtcNow, question.CreatedAt);
        Assert.Equal(
            new[] { ("Jakarta", true, 1), ("Bandung", false, 2), ("Surabaya", false, 3) },
            question.Options.OrderBy(o => o.SortOrder).Select(o => (o.Text, o.IsCorrect, o.SortOrder)).ToArray());
    }

    [Fact]
    public async Task Editing_keeps_the_question_id_and_the_ids_of_kept_options()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var topicId = await CreateTopicAsync();
        using var created = await admin.PostFormAsync("/admin/soal/baru", Form(topicId, QuestionType.SingleChoice, "Soal awal",
            [(null, "A", true), (null, "B", false), (null, "C", false)]));
        var id = IdFrom(created);
        var before = await OptionsAsync(id);
        app.Clock.Advance(TimeSpan.FromHours(1));

        // Swap A and B, rename B, drop C, add D, and make D the correct answer.
        using var saved = await admin.PostFormAsync($"/admin/soal/{id}", Form(topicId, QuestionType.SingleChoice, "Soal diubah",
            [(before[1].Id, "B baru", false), (before[0].Id, "A", false), (null, "D", true)]));

        Assert.Equal($"/admin/soal/{id}", saved.Headers.Location?.OriginalString);
        var after = await OptionsAsync(id);
        Assert.Equal(
            new[] { (before[1].Id, "B baru", false), (before[0].Id, "A", false) },
            after.Take(2).Select(o => (o.Id, o.Text, o.IsCorrect)).ToArray());
        Assert.Equal(("D", true), (after[2].Text, after[2].IsCorrect));
        Assert.DoesNotContain(after, o => o.Id == before[2].Id);
        await using var db = database.CreateContext();
        var question = await db.Questions.SingleAsync(q => q.Id == id);
        Assert.Equal(("Soal diubah", app.Clock.UtcNow), (question.Text, question.UpdatedAt));
        Assert.NotEqual(question.CreatedAt, question.UpdatedAt);
    }

    [Theory]
    [MemberData(nameof(InvalidQuestions))]
    public async Task Every_validation_rule_shows_an_indonesian_message(string caseName, QuestionType type, (long?, string, bool)[] options, string expected)
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var topicId = await CreateTopicAsync();
        var text = $"Soal tidak valid {caseName} {TestData.Unique()}";

        using var response = await admin.PostFormAsync("/admin/soal/baru", Form(topicId, type, text, options));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, html, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.False(await db.Questions.AnyAsync(q => q.Text == text));
    }

    public static TheoryData<string, QuestionType, (long?, string, bool)[], string> InvalidQuestions => new()
    {
        { "single-two-correct", QuestionType.SingleChoice, [(null, "A", true), (null, "B", true)], "Soal pilihan ganda harus punya tepat 1 jawaban benar." },
        { "single-one-option", QuestionType.SingleChoice, [(null, "A", true)], "Soal pilihan ganda harus punya 2 sampai 6 pilihan." },
        { "single-seven", QuestionType.SingleChoice, Many(7), "Soal pilihan ganda harus punya 2 sampai 6 pilihan." },
        { "multi-none-correct", QuestionType.MultiSelect, [(null, "A", false), (null, "B", false)], "Tandai minimal 1 jawaban benar." },
        { "multi-nine", QuestionType.MultiSelect, Many(9), "Soal pilihan ganda kompleks harus punya 2 sampai 8 pilihan." },
        { "truefalse-texts", QuestionType.TrueFalse, [(null, "Ya", true), (null, "Tidak", false)], "Soal Benar/Salah harus punya tepat dua pilihan: Benar lalu Salah." },
        { "truefalse-both", QuestionType.TrueFalse, [(null, "Benar", true), (null, "Salah", true)], "Soal Benar/Salah harus punya tepat 1 jawaban benar." },
        { "blank-option", QuestionType.SingleChoice, [(null, "A", true), (null, " ", false)], "Teks pilihan 2 wajib diisi." },
        { "duplicate-option", QuestionType.SingleChoice, [(null, "Jakarta", true), (null, "jakarta", false)], "Pilihan 2 sama dengan pilihan lain." },
    };

    [Fact]
    public async Task Missing_text_explanation_and_a_bad_key_fact_show_indonesian_messages()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var topicId = await CreateTopicAsync();
        var form = Form(topicId, QuestionType.SingleChoice, " ", [(null, "A", true), (null, "B", false)]);
        form["Input.Explanation"] = "";
        form["Input.KeyFact"] = new string('k', 201);
        form["Input.Difficulty"] = "6";

        using var response = await admin.PostFormAsync("/admin/soal/baru", form);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("Teks soal wajib diisi.", html, StringComparison.Ordinal);
        Assert.Contains("Pembahasan wajib diisi.", html, StringComparison.Ordinal);
        Assert.Contains("Fakta kunci (opsional) maksimal 200 karakter.", html, StringComparison.Ordinal);
        Assert.Contains("Tingkat kesulitan harus 1 sampai 5.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Retiring_keeps_the_question_in_its_exam_and_in_past_attempts()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (examId, questionId, optionIds) = await CreateAnsweredExamQuestionAsync();
        var options = await OptionsAsync(questionId);

        using var saved = await admin.PostFormAsync($"/admin/soal/{questionId}", Form(await TopicOfAsync(questionId),
            QuestionType.SingleChoice, "Soal nomor 1?", options.Select(o => ((long?)o.Id, o.Text, o.IsCorrect)).ToArray(), QuestionStatus.Retired));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(QuestionStatus.Retired, (await db.Questions.SingleAsync(q => q.Id == questionId)).Status);
        Assert.True(await db.ExamQuestions.AnyAsync(eq => eq.ExamId == examId && eq.QuestionId == questionId));
        Assert.True(await db.AttemptItems.AnyAsync(i => i.QuestionId == questionId));
        Assert.True(await db.AttemptItemOptions.AnyAsync(o => o.QuestionOptionId == optionIds[0]));
        var page = await admin.GetStringAsync(new Uri($"/admin/soal/{questionId}", UriKind.Relative));
        Assert.Contains("Dipakai di 1 ujian.", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_option_used_in_past_answers_cannot_be_removed()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (_, questionId, optionIds) = await CreateAnsweredExamQuestionAsync();
        var options = await OptionsAsync(questionId);
        var withoutAnswered = options.Where(o => o.Id != optionIds[0]).Select(o => ((long?)o.Id, o.Text, o.IsCorrect || o.Id == options[1].Id)).ToArray();

        using var response = await admin.PostFormAsync($"/admin/soal/{questionId}", Form(await TopicOfAsync(questionId), QuestionType.SingleChoice, "Soal nomor 1?", withoutAnswered));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sudah dipakai dalam jawaban latihan", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(4, (await OptionsAsync(questionId)).Count);
    }

    [Fact]
    public async Task List_filters_by_topic_status_type_and_text()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var topicId = await CreateTopicAsync();
        var token = TestData.Unique();
        await using (var db = database.CreateContext())
        {
            db.Questions.AddRange(
                NewQuestion(topicId, $"Alpha {token}", QuestionType.SingleChoice, QuestionStatus.Active),
                NewQuestion(topicId, $"Beta {token}", QuestionType.MultiSelect, QuestionStatus.Active),
                NewQuestion(topicId, $"Gamma {token}", QuestionType.SingleChoice, QuestionStatus.Retired));
            await db.SaveChangesAsync();
        }

        Assert.Equal(3, await CountAsync(admin, $"?topik={topicId}"));
        Assert.Equal(2, await CountAsync(admin, $"?topik={topicId}&status=Active"));
        Assert.Equal(1, await CountAsync(admin, $"?topik={topicId}&jenis=MultiSelect"));
        Assert.Equal(1, await CountAsync(admin, $"?q=gamma+{token}"));
        Assert.Equal(0, await CountAsync(admin, $"?topik={topicId}&status=Draft"));
    }

    [Fact]
    public async Task List_pages_by_twenty()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var topicId = await CreateTopicAsync();
        await using (var db = database.CreateContext())
        {
            db.Questions.AddRange(Enumerable.Range(1, 25).Select(i => NewQuestion(topicId, $"Halaman {i}", QuestionType.SingleChoice, QuestionStatus.Active)));
            await db.SaveChangesAsync();
        }

        var first = await admin.GetStringAsync(new Uri($"/admin/soal?topik={topicId}", UriKind.Relative));
        var second = await admin.GetStringAsync(new Uri($"/admin/soal?topik={topicId}&hal=2", UriKind.Relative));

        Assert.Equal(20, Rows(first));
        Assert.Contains("Halaman 1 dari 2", first, StringComparison.Ordinal);
        Assert.Equal(5, Rows(second));
        Assert.Contains("Halaman 2 dari 2", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_question_is_404_and_learners_get_403()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var missing = await admin.GetAsync(new Uri("/admin/soal/999999999", UriKind.Relative));
        using var forbidden = await learner.GetAsync(new Uri("/admin/soal", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private async Task<long> CreateTopicAsync()
    {
        await using var db = database.CreateContext();
        var topic = new Topic { Subject = new Subject { Name = $"Subjek {TestData.Unique()}" }, Name = "Topik uji" };
        db.Topics.Add(topic);
        await db.SaveChangesAsync();
        return topic.Id;
    }

    private async Task<long> TopicOfAsync(long questionId)
    {
        await using var db = database.CreateContext();
        return (await db.Questions.SingleAsync(q => q.Id == questionId)).TopicId;
    }

    /// <summary>A published exam whose first question was answered (first option chosen) in a trial attempt.</summary>
    private async Task<(long ExamId, long QuestionId, long[] OptionIds)> CreateAnsweredExamQuestionAsync()
    {
        await using var db = database.CreateContext();
        var exam = await db.SaveAsync<Exam>(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1));
        var question = exam.Questions.Single().Question!;
        var optionIds = question.Options.OrderBy(o => o.SortOrder).Select(o => o.Id).ToArray();
        var attempt = new Attempt
        {
            ExamId = exam.Id,
            Source = AttemptSource.Trial,
            Mode = AttemptMode.Practice,
            StartedAt = TestData.Now,
            LastSavedAt = TestData.Now,
        };
        var item = new AttemptItem { QuestionId = question.Id, Position = 1, IsAnswered = true, IsCorrect = true };
        item.SelectedOptions.Add(new AttemptItemOption { QuestionOptionId = optionIds[0] });
        attempt.Items.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return (exam.Id, question.Id, optionIds);
    }

    private async Task<List<QuestionOption>> OptionsAsync(long questionId)
    {
        await using var db = database.CreateContext();
        return await db.QuestionOptions.Where(o => o.QuestionId == questionId).OrderBy(o => o.SortOrder).ToListAsync();
    }

    private static Question NewQuestion(long topicId, string text, QuestionType type, QuestionStatus status)
    {
        var question = new Question
        {
            TopicId = topicId,
            QuestionType = type,
            Text = text,
            Explanation = "Pembahasan.",
            Status = status,
            CreatedAt = TestData.Now,
            UpdatedAt = TestData.Now,
        };
        question.Options.Add(new QuestionOption { Text = "A", IsCorrect = true, SortOrder = 1 });
        question.Options.Add(new QuestionOption { Text = "B", SortOrder = 2 });
        return question;
    }

    private static async Task<int> CountAsync(HttpClient admin, string query)
    {
        var html = await admin.GetStringAsync(new Uri($"/admin/soal{query}", UriKind.Relative));
        var start = html.IndexOf("data-testid=\"question-total\">", StringComparison.Ordinal) + "data-testid=\"question-total\">".Length;
        return int.Parse(html[start..html.IndexOf(' ', start)], CultureInfo.InvariantCulture);
    }

    /// <summary>Question rows link to <c>/admin/soal/{id}</c>; "baru" and "impor" are not rows.</summary>
    private static int Rows(string html) => System.Text.RegularExpressions.Regex.Count(html, "href=\"/admin/soal/[0-9]+\"");

    private static long IdFrom(HttpResponseMessage response) =>
        long.Parse(response.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);

    private static (long?, string, bool)[] Many(int count) =>
        Enumerable.Range(1, count).Select(i => ((long?)null, $"Pilihan {i}", i == 1)).ToArray();

    private static Dictionary<string, string> Form(
        long topicId, QuestionType type, string text, (long? Id, string Text, bool Correct)[] options, QuestionStatus status = QuestionStatus.Active)
    {
        var form = new Dictionary<string, string>
        {
            ["Input.TopicId"] = topicId.ToString(CultureInfo.InvariantCulture),
            ["Input.QuestionType"] = type.ToString(),
            ["Input.Text"] = text,
            ["Input.Explanation"] = "Jakarta, sejak 1961.",
            ["Input.KeyFact"] = "",
            ["Input.Hint"] = "Kalikan saja.",
            ["Input.Difficulty"] = "2",
            ["Input.SourceNote"] = "Kisi-kisi TWK 2026",
            ["Input.Status"] = status.ToString(),
        };
        for (var i = 0; i < options.Length; i++)
        {
            form[$"Input.Options[{i}].Id"] = options[i].Id?.ToString(CultureInfo.InvariantCulture) ?? "";
            form[$"Input.Options[{i}].Text"] = options[i].Text;
            if (options[i].Correct)
            {
                form[$"Input.Options[{i}].IsCorrect"] = "true";
            }
        }

        return form;
    }
}

internal static class FormExtensions
{
    public static Dictionary<string, string> WithKeyFact(this Dictionary<string, string> form, string keyFact)
    {
        form["Input.KeyFact"] = keyFact;
        return form;
    }
}
