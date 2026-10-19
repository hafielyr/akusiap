using System.Net;
using System.Text.Json;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Sharing;

/// <summary>SHR-03: guests take a shared exam with a name (shown to the creator); signed-in users skip it; flag off is 404.</summary>
[Collection(UsesDatabase.Name)]
public sealed class SharedExamStartTests(PostgresFixture database)
{
    [Fact]
    public async Task Without_a_name_a_guest_cannot_start_and_with_one_the_attempt_is_shared()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Features:Sharing", "true"));
        var owner = TestData.User().Build();
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(owner);
        }

        var (examId, token) = await SeedSharedExamAsync(owner);
        using var guest = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var link = $"/s/{token}";

        var landing = await guest.GetStringAsync(new Uri(link, UriKind.Relative));
        using var nameless = await guest.PostFormAsync(link, new Dictionary<string, string> { ["GuestName"] = "  " });
        using var named = await guest.PostFormAsync(link, new Dictionary<string, string> { ["GuestName"] = " Sari " });

        Assert.Contains("Nama dan skor Anda akan terlihat oleh pembuat ujian.", landing, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, nameless.StatusCode);
        Assert.Contains("data-testid=\"error-name\"", await nameless.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, named.StatusCode);
        await using var check = database.CreateContext();
        var attempt = await check.Attempts.Include(a => a.Items).SingleAsync(a => a.ExamId == examId);
        Assert.Equal((AttemptSource.Shared, "Sari", (Guid?)null), (attempt.Source, attempt.GuestName, attempt.UserId));
        Assert.NotNull(attempt.GuestToken);
        Assert.Equal(2, attempt.Items.Count);
        Assert.Equal("AfterSubmit", JsonDocument.Parse(attempt.RulesSnapshot).RootElement.GetProperty("showAnswers").GetString());
        Assert.Equal($"/sesi/{attempt.Id}", named.Headers.Location!.OriginalString);
        using var player = await guest.GetAsync(named.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, player.StatusCode);
    }

    [Fact]
    public async Task Signed_in_users_skip_the_name_and_the_flag_off_gives_404()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var owner = TestData.User().Build();
        var taker = TestData.User().Build();
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(owner);
        }

        var (examId, token) = await SeedSharedExamAsync(owner);
        using var offClient = app.CreateClient();
        using var off = await offClient.GetAsync(new Uri($"/s/{token}", UriKind.Relative));
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Features:Sharing", "true"));
        using var signedIn = await site.CreateSignedInClientAsync(taker, RoleNames.Learner);

        var landing = await signedIn.GetStringAsync(new Uri($"/s/{token}", UriKind.Relative));
        using var start = await signedIn.PostFormAsync($"/s/{token}", new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.NotFound, off.StatusCode);
        Assert.DoesNotContain("name=\"GuestName\"", landing, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        await using var check = database.CreateContext();
        var attempt = await check.Attempts.SingleAsync(a => a.ExamId == examId);
        Assert.Equal((AttemptSource.Shared, taker.Id, (string?)null), (attempt.Source, attempt.UserId!.Value, attempt.GuestName));
    }

    private async Task<(long ExamId, string Token)> SeedSharedExamAsync(User owner)
    {
        var token = Akusiap.Application.Sharing.ShareTokens.New();
        var exam = TestData.Exam().OwnedBy(owner).WithQuestions(2).Build();
        exam.Status = ExamStatus.Published;
        exam.IsShared = true;
        exam.ShareToken = token;
        exam.RulePreset!.ShowAnswers = ShowAnswers.Never;
        await using var db = database.CreateContext();
        return ((await db.SaveAsync(exam)).Id, token);
    }
}
