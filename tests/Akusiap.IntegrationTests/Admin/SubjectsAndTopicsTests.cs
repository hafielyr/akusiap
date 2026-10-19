using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>ADM-T1: admin shell, subject and topic create/edit/list, and delete rules.</summary>
[Collection(UsesDatabase.Name)]
public sealed class SubjectsAndTopicsTests(PostgresFixture database)
{
    [Fact]
    public async Task Admin_creates_a_subject_and_sees_it_in_the_list()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var name = $"Seleksi CPNS {TestData.Unique()}";

        using var created = await admin.PostFormAsync("/admin/subjek/baru", SubjectForm(name, "Tes CPNS 2026"));
        var detail = await admin.GetStringAsync(created.Headers.Location);
        var list = await admin.GetStringAsync(new Uri("/admin/subjek", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Matches(@"^/admin/subjek/\d+$", created.Headers.Location!.OriginalString);
        Assert.Contains("Perubahan disimpan.", detail, StringComparison.Ordinal);
        Assert.Contains(name, list, StringComparison.Ordinal);
        Assert.Contains("0 topik · 0 ujian", list, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"admin-nav\"", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subject_without_a_name_shows_an_indonesian_error()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);

        using var response = await admin.PostFormAsync("/admin/subjek/baru", SubjectForm("   ", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nama subjek wajib diisi.", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Subject_names_are_unique_ignoring_case()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var name = $"UTBK {TestData.Unique()}";
        using var first = await admin.PostFormAsync("/admin/subjek/baru", SubjectForm(name, null));

        using var second = await admin.PostFormAsync("/admin/subjek/baru", SubjectForm(name.ToUpperInvariant(), null));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Contains("Subjek dengan nama ini sudah ada.", await second.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_edits_a_subject_and_it_keeps_its_id()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var subjectId = await CreateSubjectAsync(admin, $"Lama {TestData.Unique()}");
        var newName = $"Baru {TestData.Unique()}";

        using var saved = await admin.PostFormAsync($"/admin/subjek/{subjectId}", SubjectForm(newName, "Diperbarui"));

        Assert.Equal($"/admin/subjek/{subjectId}", saved.Headers.Location?.OriginalString);
        await using var db = database.CreateContext();
        var subject = await db.Subjects.SingleAsync(s => s.Id == subjectId);
        Assert.Equal((newName, "Diperbarui"), (subject.Name, subject.Description));
    }

    [Fact]
    public async Task Admin_adds_renames_and_moves_a_topic()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var subjectId = await CreateSubjectAsync(admin, $"CPNS {TestData.Unique()}");
        var otherSubjectId = await CreateSubjectAsync(admin, $"PPPK {TestData.Unique()}");

        using var added = await admin.PostFormAsync(
            $"/admin/subjek/{subjectId}", TopicForm("Tes Wawasan Kebangsaan", "TWK"), postUrl: $"/admin/subjek/{subjectId}?handler=AddTopic");
        var detail = await admin.GetStringAsync(new Uri($"/admin/subjek/{subjectId}", UriKind.Relative));
        await using var db = database.CreateContext();
        var topic = await db.Topics.SingleAsync(t => t.SubjectId == subjectId);

        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Contains("Topik ditambahkan.", detail, StringComparison.Ordinal);
        Assert.Contains("Tes Wawasan Kebangsaan", detail, StringComparison.Ordinal);

        using var moved = await admin.PostFormAsync(
            $"/admin/topik/{topic.Id}",
            new Dictionary<string, string> { ["Input.SubjectId"] = $"{otherSubjectId}", ["Input.Name"] = "TWK", ["Input.Description"] = "" });

        Assert.Equal($"/admin/subjek/{otherSubjectId}", moved.Headers.Location?.OriginalString);
        await using var check = database.CreateContext();
        var saved = await check.Topics.SingleAsync(t => t.Id == topic.Id);
        Assert.Equal((otherSubjectId, "TWK", (string?)null), (saved.SubjectId, saved.Name, saved.Description));
    }

    [Fact]
    public async Task Topic_names_are_unique_within_a_subject()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var subjectId = await CreateSubjectAsync(admin, $"CPNS {TestData.Unique()}");
        var addUrl = $"/admin/subjek/{subjectId}?handler=AddTopic";
        using var first = await admin.PostFormAsync($"/admin/subjek/{subjectId}", TopicForm("TIU", null), postUrl: addUrl);

        using var duplicate = await admin.PostFormAsync($"/admin/subjek/{subjectId}", TopicForm("tiu", null), postUrl: addUrl);

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("Topik dengan nama ini sudah ada di subjek tersebut.", await duplicate.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Topic_in_use_cannot_be_deleted_and_says_why()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var topicId = await CreateTopicWithQuestionsAsync(questions: 2);

        var page = await admin.GetStringAsync(new Uri($"/admin/topik/{topicId}", UriKind.Relative));
        using var response = await admin.PostFormAsync(
            $"/admin/topik/{topicId}", new Dictionary<string, string>(), postUrl: $"/admin/topik/{topicId}?handler=Delete");
        var html = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Hapus topik", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Topik ini dipakai oleh 2 soal, jadi tidak bisa dihapus.", html, StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.True(await db.Topics.AnyAsync(t => t.Id == topicId));
    }

    [Fact]
    public async Task Unused_topic_and_then_its_empty_subject_can_be_deleted()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var subjectId = await CreateSubjectAsync(admin, $"Hapus {TestData.Unique()}");
        using var _ = await admin.PostFormAsync($"/admin/subjek/{subjectId}", TopicForm("Sementara", null), postUrl: $"/admin/subjek/{subjectId}?handler=AddTopic");
        await using var db = database.CreateContext();
        var topicId = (await db.Topics.SingleAsync(t => t.SubjectId == subjectId)).Id;

        using var subjectBlocked = await admin.PostFormAsync(
            $"/admin/subjek/{subjectId}", new Dictionary<string, string>(), postUrl: $"/admin/subjek/{subjectId}?handler=Delete");
        using var topicDeleted = await admin.PostFormAsync(
            $"/admin/topik/{topicId}", new Dictionary<string, string>(), postUrl: $"/admin/topik/{topicId}?handler=Delete");
        using var subjectDeleted = await admin.PostFormAsync(
            $"/admin/subjek/{subjectId}", new Dictionary<string, string>(), postUrl: $"/admin/subjek/{subjectId}?handler=Delete");

        Assert.Contains("Subjek ini masih punya 1 topik dan 0 ujian", await subjectBlocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal($"/admin/subjek/{subjectId}", topicDeleted.Headers.Location?.OriginalString);
        Assert.Equal("/admin/subjek", subjectDeleted.Headers.Location?.OriginalString);
        await using var check = database.CreateContext();
        Assert.False(await check.Topics.AnyAsync(t => t.Id == topicId));
        Assert.False(await check.Subjects.AnyAsync(s => s.Id == subjectId));
    }

    [Fact]
    public async Task Unknown_subject_or_topic_is_404()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);

        using var subject = await admin.GetAsync(new Uri("/admin/subjek/999999999", UriKind.Relative));
        using var topic = await admin.GetAsync(new Uri("/admin/topik/999999999", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, subject.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, topic.StatusCode);
    }

    [Fact]
    public async Task Learner_cannot_open_or_post_to_subject_pages()
    {
        await using var app = await StartAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var list = await learner.GetAsync(new Uri("/admin/subjek", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
    }

    [Fact]
    public async Task Admin_sees_the_admin_link_in_the_header_and_learners_do_not()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        Assert.Contains("data-testid=\"admin-link\"", await admin.GetStringAsync(new Uri("/", UriKind.Relative)), StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"admin-link\"", await learner.GetStringAsync(new Uri("/ujian", UriKind.Relative)), StringComparison.Ordinal);
    }

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private static async Task<long> CreateSubjectAsync(HttpClient admin, string name)
    {
        using var response = await admin.PostFormAsync("/admin/subjek/baru", SubjectForm(name, null));
        return long.Parse(response.Headers.Location!.OriginalString.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<long> CreateTopicWithQuestionsAsync(int questions)
    {
        await using var db = database.CreateContext();
        var topic = new Topic { Subject = new Subject { Name = $"Dipakai {TestData.Unique()}" }, Name = "Penalaran" };
        for (var i = 1; i <= questions; i++)
        {
            db.Questions.Add(new Question
            {
                Topic = topic,
                QuestionType = QuestionType.SingleChoice,
                Text = $"Soal {i}?",
                Explanation = "Pembahasan.",
                CreatedAt = TestData.Now,
                UpdatedAt = TestData.Now,
            });
        }

        await db.SaveChangesAsync();
        return topic.Id;
    }

    private static Dictionary<string, string> SubjectForm(string name, string? description) => new()
    {
        ["Input.Name"] = name,
        ["Input.Description"] = description ?? string.Empty,
    };

    private static Dictionary<string, string> TopicForm(string name, string? description) => new()
    {
        ["NewTopic.Name"] = name,
        ["NewTopic.Description"] = description ?? string.Empty,
    };
}
