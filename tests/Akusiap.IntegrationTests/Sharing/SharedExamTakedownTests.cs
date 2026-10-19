using System.Net;
using Akusiap.Domain.Access;
using Akusiap.Domain.Content;
using Akusiap.Domain.Moderation;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Sharing;

/// <summary>SHR-02: anyone with the link reports a shared exam; an admin takes it down (link "tidak tersedia", no start) and restores it.</summary>
[Collection(UsesDatabase.Name)]
public sealed class SharedExamTakedownTests(PostgresFixture database)
{
    [Fact]
    public async Task A_taken_down_exam_shows_unavailable_and_cannot_start()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Features:Sharing", "true"));
        var owner = TestData.User().Build();
        using var ownerClient = await site.CreateSignedInClientAsync(owner, RoleNames.Learner);
        using var admin = await site.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        using var guest = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (examId, token) = await SeedSharedExamAsync(owner);
        var link = $"/s/{token}";

        var landing = await guest.GetStringAsync(new Uri(link, UriKind.Relative));
        using var reported = await guest.PostFormAsync($"{link}/lapor", new Dictionary<string, string> { ["Reason"] = "Copyright", ["Details"] = "Disalin dari buku." });
        var queue = await admin.GetStringAsync(new Uri("/admin/laporan?tab=ujian", UriKind.Relative));
        using var takeDown = await admin.PostFormAsync("/admin/laporan?tab=ujian", new Dictionary<string, string> { ["examId"] = examId.ToString(System.Globalization.CultureInfo.InvariantCulture) }, "/admin/laporan?handler=TakeDown");
        var unavailable = await guest.GetStringAsync(new Uri(link, UriKind.Relative));
        var ownerSetup = await ownerClient.GetAsync(new Uri($"/ujian/{examId}/mulai", UriKind.Relative));

        Assert.Contains("data-testid=\"shared-exam\"", landing, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, reported.StatusCode);
        Assert.Contains($"data-exam=\"{examId}\"", queue, StringComparison.Ordinal);
        Assert.Contains("Disalin dari buku.", queue, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, takeDown.StatusCode);
        Assert.Contains("Ujian tidak tersedia", unavailable, StringComparison.Ordinal);
        await using (var db = database.CreateContext())
        {
            var exam = await db.Exams.AsNoTracking().SingleAsync(e => e.Id == examId);
            Assert.Equal(ExamStatus.TakenDown, exam.Status);
            var report = await db.ContentReports.SingleAsync(r => r.ExamId == examId);
            Assert.Equal((ContentReportStatus.TakenDown, (Guid?)null), (report.Status, report.ReporterUserId));
            Assert.NotNull(report.ResolvedByUserId);
            var facts = new ExamAccessFacts(exam.Id, exam.OwnerUserId, exam.OwnerKind, exam.Status, exam.IsFree, exam.IsShared, exam.ShareToken, false);
            Assert.False(EntitlementPolicy.Evaluate(ViewerContext.Guest, facts, token, TestData.Now).CanStart);
            Assert.False(EntitlementPolicy.Evaluate(new ViewerContext(owner.Id, false, [], []), facts, null, TestData.Now).CanStart);
        }

        using var restore = await admin.PostFormAsync("/admin/laporan?tab=ujian", new Dictionary<string, string> { ["examId"] = examId.ToString(System.Globalization.CultureInfo.InvariantCulture) }, "/admin/laporan?handler=Restore");
        var back = await guest.GetStringAsync(new Uri(link, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, restore.StatusCode);
        Assert.Contains("data-testid=\"shared-exam\"", back, StringComparison.Ordinal);
        ownerSetup.Dispose();
    }

    [Fact]
    public async Task Links_are_404_with_the_flag_off_or_when_not_shared()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var owner = TestData.User().Build();
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(owner);
        }

        var (examId, token) = await SeedSharedExamAsync(owner);
        using var flagOff = app.CreateClient();
        using var off = await flagOff.GetAsync(new Uri($"/s/{token}", UriKind.Relative));
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Features:Sharing", "true"));
        using var flagOn = site.CreateClient();
        using var wrong = await flagOn.GetAsync(new Uri($"/s/{token.ToUpperInvariant()}x", UriKind.Relative));
        await using (var db = database.CreateContext())
        {
            await db.Exams.Where(e => e.Id == examId).ExecuteUpdateAsync(s => s.SetProperty(e => e.IsShared, false));
        }

        using var stopped = await flagOn.GetAsync(new Uri($"/s/{token}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, off.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, stopped.StatusCode);
    }

    private async Task<(long ExamId, string Token)> SeedSharedExamAsync(User owner)
    {
        var token = Akusiap.Application.Sharing.ShareTokens.New();
        var exam = TestData.Exam().OwnedBy(owner).WithQuestions(2).Build();
        exam.Status = ExamStatus.Published;
        exam.IsShared = true;
        exam.ShareToken = token;
        await using var db = database.CreateContext();
        return ((await db.SaveAsync(exam)).Id, token);
    }
}
