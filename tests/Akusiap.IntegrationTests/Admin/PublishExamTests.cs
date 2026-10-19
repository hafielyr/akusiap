using System.Net;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>ADM-T6: publish validation, publish, unpublish and what that means for access.</summary>
[Collection(UsesDatabase.Name)]
public sealed class PublishExamTests(PostgresFixture database)
{
    [Fact]
    public async Task Publishing_an_invalid_exam_lists_every_problem()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long examId;
        await using (var db = database.CreateContext())
        {
            var exam = TestData.Exam().Draft().Paid(49_000).WithQuestions(35, trial: 10).Build();
            exam.PriceIdr = null;
            exam.AccessDays = null;
            examId = (await db.SaveAsync(exam)).Id;
        }

        using var response = await PublishAsync(admin, examId);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ujian belum bisa diterbitkan:", html, StringComparison.Ordinal);
        Assert.Contains("Isi harga ujian berbayar.", html, StringComparison.Ordinal);
        Assert.Contains("Isi masa akses ujian berbayar.", html, StringComparison.Ordinal);
        Assert.Contains("Ujian berbayar dengan 35 soal aktif perlu 20–35 soal coba aktif; sekarang 10.", html, StringComparison.Ordinal);
        Assert.Equal(ExamStatus.Draft, await StatusAsync(examId));
    }

    [Fact]
    public async Task An_exam_without_active_questions_cannot_be_published()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync<Exam>(TestData.Exam().Draft())).Id;
        }

        using var response = await PublishAsync(admin, examId);

        Assert.Contains("Tambahkan minimal 1 soal aktif.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(ExamStatus.Draft, await StatusAsync(examId));
    }

    [Fact]
    public async Task Retired_questions_do_not_count_towards_the_trial_rule()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long examId;
        await using (var db = database.CreateContext())
        {
            // 30 questions with 20 trial would pass; retiring one non-trial question leaves 29 active, which then need all trial.
            var exam = await db.SaveAsync<Exam>(TestData.Exam().Draft().Paid(49_000).WithQuestions(30, trial: 20));
            examId = exam.Id;
            exam.Questions.OrderBy(q => q.SortOrder).Last().Question!.Status = QuestionStatus.Retired;
            await db.SaveChangesAsync();
        }

        using var response = await PublishAsync(admin, examId);

        Assert.Contains("Ujian berbayar dengan 29 soal aktif (kurang dari 30) perlu semua soalnya jadi soal coba; sekarang 20.",
            WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(ExamStatus.Draft, await StatusAsync(examId));
    }

    [Fact]
    public async Task A_ready_exam_is_published_then_unpublished_then_published_again()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync<Exam>(TestData.Exam().Draft().Paid(49_000).WithQuestions(40, trial: 30))).Id;
        }

        var before = await admin.GetStringAsync(new Uri($"/admin/ujian/{examId}", UriKind.Relative));
        using var published = await PublishAsync(admin, examId);
        var afterPublish = await admin.GetStringAsync(published.Headers.Location);
        Assert.Contains("data-testid=\"publish-ready\"", before, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.Contains("Ujian diterbitkan dan tampil di katalog.", afterPublish, StringComparison.Ordinal);
        Assert.Equal(ExamStatus.Published, await StatusAsync(examId));
        Assert.Contains("Terbit", await admin.GetStringAsync(new Uri("/admin/ujian", UriKind.Relative)), StringComparison.Ordinal);

        using var unpublished = await admin.PostFormAsync($"/admin/ujian/{examId}", new Dictionary<string, string>(), postUrl: $"/admin/ujian/{examId}?handler=Unpublish");
        Assert.Contains("Penerbitan dibatalkan.", await admin.GetStringAsync(unpublished.Headers.Location), StringComparison.Ordinal);
        Assert.Equal(ExamStatus.Unpublished, await StatusAsync(examId));

        using var republished = await PublishAsync(admin, examId);
        Assert.Equal(HttpStatusCode.Redirect, republished.StatusCode);
        Assert.Equal(ExamStatus.Published, await StatusAsync(examId));
    }

    [Fact]
    public async Task A_free_exam_with_one_active_question_can_be_published()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync<Exam>(TestData.Exam().Draft().Free().WithQuestions(1))).Id;
        }

        using var response = await PublishAsync(admin, examId);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(ExamStatus.Published, await StatusAsync(examId));
    }

    [Fact]
    public async Task Publishing_opens_the_trial_to_guests_and_unpublishing_closes_it()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync<Exam>(TestData.Exam().Draft().Paid(49_000).WithQuestions(40, trial: 30))).Id;
        }

        Assert.Equal(AccessReason.NotPublished, (await GuestDecisionAsync(examId)).Reason);
        using (await PublishAsync(admin, examId)) { }
        Assert.Equal((AccessLevel.Trial, QuestionScope.TrialOnly), ((await GuestDecisionAsync(examId)).Level, (await GuestDecisionAsync(examId)).Scope));
        using (await admin.PostFormAsync($"/admin/ujian/{examId}", new Dictionary<string, string>(), postUrl: $"/admin/ujian/{examId}?handler=Unpublish")) { }
        Assert.Equal(AccessReason.NotPublished, (await GuestDecisionAsync(examId)).Reason);
    }

    [Fact]
    public async Task Draft_exam_page_shows_what_is_missing_before_publishing()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long examId;
        await using (var db = database.CreateContext())
        {
            examId = (await db.SaveAsync<Exam>(TestData.Exam().Draft().Paid(49_000))).Id;
        }

        var page = WebUtility.HtmlDecode(await admin.GetStringAsync(new Uri($"/admin/ujian/{examId}", UriKind.Relative)));

        Assert.Contains("Sebelum terbit, lengkapi dulu:", page, StringComparison.Ordinal);
        Assert.Contains("Tambahkan minimal 1 soal aktif.", page, StringComparison.Ordinal);
    }

    // ---------- Helpers ----------

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private static Task<HttpResponseMessage> PublishAsync(HttpClient admin, long examId) =>
        admin.PostFormAsync($"/admin/ujian/{examId}", new Dictionary<string, string>(), postUrl: $"/admin/ujian/{examId}?handler=Publish");

    private async Task<ExamStatus> StatusAsync(long examId)
    {
        await using var db = database.CreateContext();
        return (await db.Exams.SingleAsync(e => e.Id == examId)).Status;
    }

    /// <summary>The entitlement decision for a guest, from the exam as stored (what the catalog and trial will use).</summary>
    private async Task<AccessDecision> GuestDecisionAsync(long examId)
    {
        await using var db = database.CreateContext();
        var exam = await db.Exams.SingleAsync(e => e.Id == examId);
        var hasTrial = await db.ExamQuestions.AnyAsync(eq => eq.ExamId == examId && eq.IsTrial && eq.Question!.Status == QuestionStatus.Active);
        var facts = new ExamAccessFacts(exam.Id, exam.OwnerUserId, exam.OwnerKind, exam.Status, exam.IsFree, exam.IsShared, exam.ShareToken, hasTrial);
        return EntitlementPolicy.Evaluate(ViewerContext.Guest, facts, null, TestData.Now);
    }
}
