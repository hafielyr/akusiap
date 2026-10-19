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

/// <summary>ADM-T3: exam create/edit/list for app exams, paid-exam rules and delete rules.</summary>
[Collection(UsesDatabase.Name)]
public sealed class ExamEditorTests(PostgresFixture database)
{
    [Fact]
    public async Task Admin_creates_a_paid_exam_and_it_starts_as_draft()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var title = $"Tryout SKD {TestData.Unique()}";

        using var response = await admin.PostFormAsync("/admin/ujian/baru", await FormAsync(title, isFree: false, price: "Rp49.000", accessDays: "30"));
        var id = IdFrom(response);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Ujian dibuat sebagai draf.", await admin.GetStringAsync(response.Headers.Location), StringComparison.Ordinal);
        await using var db = database.CreateContext();
        var exam = await db.Exams.SingleAsync(e => e.Id == id);
        Assert.Equal((ExamStatus.Draft, OwnerKind.App, (Guid?)null), (exam.Status, exam.OwnerKind, exam.OwnerUserId));
        Assert.Equal(((long?)49_000, (int?)30, 100, false), (exam.PriceIdr, exam.AccessDays, exam.OwnQuestionAllowance, exam.IsFree));
        Assert.Equal((title, "Latihan lengkap.", app.Clock.UtcNow), (exam.Title, exam.Description, exam.CreatedAt));
        Assert.Equal(await DefaultPresetIdAsync(), exam.RulePresetId);
        var list = await admin.GetStringAsync(new Uri("/admin/ujian", UriKind.Relative));
        Assert.Contains(title, list, StringComparison.Ordinal);
        Assert.Contains("Rp49.000", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Free_exam_ignores_price_and_access_days()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);

        using var response = await admin.PostFormAsync("/admin/ujian/baru", await FormAsync($"Gratis {TestData.Unique()}", isFree: true, price: "49000", accessDays: "30"));

        await using var db = database.CreateContext();
        var exam = await db.Exams.SingleAsync(e => e.Id == IdFrom(response));
        Assert.Equal((true, (long?)null, (int?)null, ExamStatus.Draft), (exam.IsFree, exam.PriceIdr, exam.AccessDays, exam.Status));
    }

    [Theory]
    [InlineData("", "30", "Ujian berbayar harus punya harga lebih dari Rp0.")]
    [InlineData("0", "30", "Ujian berbayar harus punya harga lebih dari Rp0.")]
    [InlineData("49000", "", "Ujian berbayar harus punya masa akses lebih dari 0 hari.")]
    [InlineData("49000", "0", "Ujian berbayar harus punya masa akses lebih dari 0 hari.")]
    [InlineData("49,5", "30", "Tulis harga dalam rupiah tanpa desimal, misalnya 49000 atau Rp49.000.")]
    [InlineData("49000", "abc", "Ujian berbayar harus punya masa akses lebih dari 0 hari.")]
    public async Task Paid_exam_requires_a_price_and_access_days_above_zero(string price, string accessDays, string expected)
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var title = $"Tanpa harga {TestData.Unique()}";

        using var response = await admin.PostFormAsync("/admin/ujian/baru", await FormAsync(title, isFree: false, price: price, accessDays: accessDays));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.False(await db.Exams.AnyAsync(e => e.Title == title));
    }

    [Fact]
    public async Task Missing_title_shows_an_indonesian_message()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);

        using var response = await admin.PostFormAsync("/admin/ujian/baru", await FormAsync(" ", isFree: true, price: "", accessDays: ""));

        Assert.Contains("Judul ujian wajib diisi.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editing_keeps_the_id_and_the_status()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long id;
        await using (var db = database.CreateContext())
        {
            id = (await db.SaveAsync<Exam>(TestData.Exam().Paid(49_000))).Id;
        }

        using var response = await admin.PostFormAsync($"/admin/ujian/{id}", await FormAsync("Judul baru", isFree: false, price: "59.000", accessDays: "60"));

        Assert.Equal($"/admin/ujian/{id}", response.Headers.Location?.OriginalString);
        await using var check = database.CreateContext();
        var exam = await check.Exams.SingleAsync(e => e.Id == id);
        Assert.Equal(("Judul baru", (long?)59_000, (int?)60, ExamStatus.Published), (exam.Title, exam.PriceIdr, exam.AccessDays, exam.Status));
    }

    [Fact]
    public async Task Unused_draft_can_be_deleted_and_its_questions_stay_in_the_bank()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long id;
        long questionId;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync<Exam>(TestData.Exam().Draft().WithQuestions(2));
            (id, questionId) = (exam.Id, exam.Questions.First().QuestionId);
        }

        using var response = await admin.PostFormAsync($"/admin/ujian/{id}", new Dictionary<string, string>(), postUrl: $"/admin/ujian/{id}?handler=Delete");

        Assert.Equal("/admin/ujian", response.Headers.Location?.OriginalString);
        await using var check = database.CreateContext();
        Assert.False(await check.Exams.AnyAsync(e => e.Id == id));
        Assert.False(await check.ExamQuestions.AnyAsync(eq => eq.ExamId == id));
        Assert.True(await check.Questions.AnyAsync(q => q.Id == questionId));
    }

    [Fact]
    public async Task Published_or_attempted_exams_cannot_be_deleted()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long publishedId;
        long attemptedId;
        await using (var db = database.CreateContext())
        {
            publishedId = (await db.SaveAsync<Exam>(TestData.Exam())).Id;
            attemptedId = (await db.SaveAsync<Exam>(TestData.Exam().Draft().WithQuestions(1, trial: 1).Paid(49_000))).Id;
            db.Attempts.Add(new Attempt
            {
                ExamId = attemptedId,
                Source = AttemptSource.Trial,
                Mode = AttemptMode.Practice,
                StartedAt = TestData.Now,
                LastSavedAt = TestData.Now,
            });
            await db.SaveChangesAsync();
        }

        var publishedPage = await admin.GetStringAsync(new Uri($"/admin/ujian/{publishedId}", UriKind.Relative));
        using var published = await admin.PostFormAsync($"/admin/ujian/{publishedId}", new Dictionary<string, string>(), postUrl: $"/admin/ujian/{publishedId}?handler=Delete");
        using var attempted = await admin.PostFormAsync($"/admin/ujian/{attemptedId}", new Dictionary<string, string>(), postUrl: $"/admin/ujian/{attemptedId}?handler=Delete");

        Assert.Contains("data-testid=\"delete-blocked\"", publishedPage, StringComparison.Ordinal);
        Assert.Contains("Ujian yang sedang terbit tidak bisa dihapus.", WebUtility.HtmlDecode(await published.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Contains("Ujian ini sudah dipakai (1 latihan, 0 pembelian)", WebUtility.HtmlDecode(await attempted.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        await using var check = database.CreateContext();
        Assert.Equal(2, await check.Exams.CountAsync(e => e.Id == publishedId || e.Id == attemptedId));
    }

    [Fact]
    public async Task Unknown_exam_is_404_and_learners_get_403()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        using var missing = await admin.GetAsync(new Uri("/admin/ujian/999999999", UriKind.Relative));
        using var forbidden = await learner.GetAsync(new Uri("/admin/ujian", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private async Task<long> DefaultPresetIdAsync()
    {
        await using var db = database.CreateContext();
        return (await db.RulePresets.SingleAsync(r => r.OwnerUserId == null && r.IsDefault)).Id;
    }

    private async Task<Dictionary<string, string>> FormAsync(string title, bool isFree, string price, string accessDays)
    {
        var form = new Dictionary<string, string>
        {
            ["Input.Title"] = title,
            ["Input.Description"] = "Latihan lengkap.",
            ["Input.SubjectId"] = "",
            ["Input.PriceIdr"] = price,
            ["Input.AccessDays"] = accessDays,
            ["Input.OwnQuestionAllowance"] = "100",
            ["Input.RulePresetId"] = (await DefaultPresetIdAsync()).ToString(CultureInfo.InvariantCulture),
        };
        if (isFree)
        {
            form["Input.IsFree"] = "true";
        }

        return form;
    }

    private static long IdFrom(HttpResponseMessage response) =>
        long.Parse(response.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);
}
