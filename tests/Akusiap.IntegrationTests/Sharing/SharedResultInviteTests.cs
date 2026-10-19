using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Akusiap.IntegrationTests.Sharing;

/// <summary>SHR-04: a shared result invites the guest to register; registering in that browser moves their attempts into the account.</summary>
[Collection(UsesDatabase.Name)]
public sealed class SharedResultInviteTests(PostgresFixture database)
{
    [Fact]
    public async Task After_registration_the_attempt_appears_in_the_users_history()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        await using var site = app.WithWebHostBuilder(b => b.UseSetting("Features:Sharing", "true"));
        var owner = TestData.User().Build();
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(owner);
        }

        var token = Akusiap.Application.Sharing.ShareTokens.New();
        var exam = TestData.Exam().OwnedBy(owner).WithQuestions(2).Build();
        exam.Status = ExamStatus.Published;
        exam.IsShared = true;
        exam.ShareToken = token;
        long trialExamId;
        await using (var db = database.CreateContext())
        {
            await db.SaveAsync(exam);
            trialExamId = (await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(2, trial: 2).Build())).Id;
        }

        using var guest = site.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var started = await guest.PostFormAsync($"/s/{token}", new Dictionary<string, string> { ["GuestName"] = "Sari" });
        var player = started.Headers.Location!.OriginalString;
        using (await guest.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit"))
        {
        }

        using var trial = await guest.PostFormAsync($"/ujian/{trialExamId}/mulai", new Dictionary<string, string> { ["Form.Mode"] = "Practice", ["Form.Untimed"] = "true" });
        var trialPlayer = trial.Headers.Location!.OriginalString;
        var result = await guest.GetStringAsync(new Uri($"{player}/hasil", UriKind.Relative));
        var review = await guest.GetStringAsync(new Uri($"{player}/pembahasan", UriKind.Relative));
        var email = $"sari-{TestData.Unique()}@contoh.id";
        using var registered = await guest.PostFormAsync("/akun/daftar", new Dictionary<string, string>
        {
            ["Input.Email"] = email, ["Input.DisplayName"] = "Sari", ["Input.Password"] = "sandi-rahasia-2026",
        });
        var history = await guest.GetStringAsync(new Uri("/learn/riwayat", UriKind.Relative));

        Assert.Contains("data-testid=\"register-invite\"", result, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"register-invite\"", review, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        Assert.Contains($"data-attempt=\"{player.Split('/')[^1]}\"", history, StringComparison.Ordinal);
        Assert.Contains($"data-attempt=\"{trialPlayer.Split('/')[^1]}\"", history, StringComparison.Ordinal);
        var after = await guest.GetStringAsync(new Uri($"{player}/hasil", UriKind.Relative));
        Assert.DoesNotContain("data-testid=\"register-invite\"", after, StringComparison.Ordinal);
    }
}
