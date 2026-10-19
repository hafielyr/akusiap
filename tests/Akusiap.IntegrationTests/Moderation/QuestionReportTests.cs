using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Moderation;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Moderation;

/// <summary>SES-11: "Laporkan soal" merges a user's repeat reports; the admin queue groups by question, newest first.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class QuestionReportTests(PostgresFixture database)
{
    [Fact]
    public async Task Duplicate_reports_from_the_same_user_on_the_same_question_are_merged()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        using var stranger = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (attemptId, questionIds) = await SeedAttemptAsync(user.Id, 2);
        var form = $"/sesi/{attemptId}/lapor/1";

        using var first = await learner.PostFormAsync(form, new Dictionary<string, string> { ["Reason"] = "WrongAnswer", ["Details"] = "Seharusnya B." });
        using var second = await learner.PostFormAsync(form, new Dictionary<string, string> { ["Reason"] = "Unclear" });
        using var missing = await learner.PostFormAsync(form, new Dictionary<string, string> { ["Details"] = "Tanpa alasan" });
        var thanks = await learner.GetStringAsync(first.Headers.Location);
        using var foreign = await stranger.GetAsync(new Uri(form, UriKind.Relative));

        Assert.Equal((HttpStatusCode.Redirect, HttpStatusCode.Redirect, HttpStatusCode.OK), (first.StatusCode, second.StatusCode, missing.StatusCode));
        Assert.Contains("data-testid=\"error-reason\"", await missing.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("data-testid=\"report-sent\"", thanks, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        await using var db = database.CreateContext();
        var report = Assert.Single(await db.ContentReports.Where(r => r.QuestionId == questionIds[0]).ToListAsync());
        Assert.Equal((user.Id, ContentReportStatus.Open), (report.ReporterUserId!.Value, report.Status));
        Assert.Equal("WrongAnswer: Seharusnya B.\nUnclear", report.Reason);
    }

    [Fact]
    public async Task The_queue_groups_by_question_newest_first_and_resolving_closes_the_group()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var first = TestData.User().Build();
        var second = TestData.User().Build();
        using var learnerA = await app.CreateSignedInClientAsync(first, RoleNames.Learner);
        using var learnerB = await app.CreateSignedInClientAsync(second, RoleNames.Learner);
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var (attemptA, questions) = await SeedAttemptAsync(first.Id, 2);
        var attemptB = await SeedAttemptForAsync(second.Id, questions);

        using (await learnerA.PostFormAsync($"/sesi/{attemptA}/lapor/1", new Dictionary<string, string> { ["Reason"] = "Typo" }))
        {
        }

        app.Clock.Advance(TimeSpan.FromMinutes(5));
        using (await learnerB.PostFormAsync($"/sesi/{attemptB}/lapor/1", new Dictionary<string, string> { ["Reason"] = "WrongAnswer" }))
        {
        }

        app.Clock.Advance(TimeSpan.FromMinutes(5));
        using (await learnerA.PostFormAsync($"/sesi/{attemptA}/lapor/2", new Dictionary<string, string> { ["Reason"] = "Other", ["Details"] = "Gambar hilang" }))
        {
        }

        var queue = await admin.GetStringAsync(new Uri("/admin/laporan", UriKind.Relative));
        var order = GroupPattern().Matches(queue).Select(m => long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.Equal(questions[1], order[0]);
        Assert.Equal(1, order.IndexOf(questions[0]));
        Assert.Contains("Gambar hilang", queue, StringComparison.Ordinal);

        using var resolve = await admin.PostFormAsync("/admin/laporan", new Dictionary<string, string>
        {
            ["questionId"] = questions[0].ToString(System.Globalization.CultureInfo.InvariantCulture), ["status"] = "Fixed",
        }, "/admin/laporan?handler=Resolve");

        Assert.Equal(HttpStatusCode.Redirect, resolve.StatusCode);
        await using var db = database.CreateContext();
        var closed = await db.ContentReports.Where(r => r.QuestionId == questions[0]).ToListAsync();
        Assert.Equal(2, closed.Count);
        Assert.All(closed, r => Assert.Equal(ContentReportStatus.Fixed, r.Status));
        Assert.All(closed, r => Assert.NotNull(r.ResolvedByUserId));
        Assert.All(closed, r => Assert.Equal(app.Clock.UtcNow, r.ResolvedAt));
        var after = await admin.GetStringAsync(new Uri("/admin/laporan", UriKind.Relative));
        Assert.DoesNotContain($"data-question=\"{questions[0]}\"", after, StringComparison.Ordinal);
        Assert.Contains($"data-question=\"{questions[1]}\"", after, StringComparison.Ordinal);
    }

    private async Task<(long AttemptId, List<long> QuestionIds)> SeedAttemptAsync(Guid userId, int count)
    {
        long examId;
        List<long> questionIds;
        await using (var db = database.CreateContext())
        {
            var exam = await db.SaveAsync(TestData.Exam().Free().WithQuestions(count).Build());
            examId = exam.Id;
            questionIds = [.. exam.Questions.OrderBy(q => q.SortOrder).Select(q => q.QuestionId)];
        }

        return (await SeedAttemptForAsync(userId, questionIds, examId), questionIds);
    }

    private async Task<long> SeedAttemptForAsync(Guid userId, List<long> questionIds, long? examId = null)
    {
        await using var db = database.CreateContext();
        examId ??= await db.ExamQuestions.Where(e => e.QuestionId == questionIds[0]).Select(e => e.ExamId).FirstAsync();
        var attempt = new Attempt
        {
            UserId = userId, ExamId = examId, Source = AttemptSource.Exam, Mode = AttemptMode.Practice, Status = AttemptStatus.Submitted,
            RulesSnapshot = "{}", StartedAt = TestData.Now, LastSavedAt = TestData.Now, SubmittedAt = TestData.Now,
        };
        for (var i = 0; i < questionIds.Count; i++)
        {
            attempt.Items.Add(new AttemptItem { QuestionId = questionIds[i], Position = i + 1 });
        }

        db.Attempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }

    [GeneratedRegex("data-testid=\"report-group\" data-question=\"(\\d+)\"")]
    private static partial Regex GroupPattern();
}
