using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Akusiap.Domain.Memorization;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Planning;

/// <summary>
/// GST-01, GST-02 (D-134, D-135): a guest takes one trial, is then asked to register, and registering or signing in
/// carries the trial into the account with its wrong answers as review cards and lands on the analysis.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed partial class GuestFunnelTests(PostgresFixture database)
{
    private const string Password = "test-sandi-2026";

    [Fact]
    public async Task A_guest_gets_one_trial_then_every_exam_asks_them_to_register()
    {
        await using var app = new AppFactory(database);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (first, second) = await TwoPaidExamsAsync();

        var player = await StartAsync(guest, first);
        using var setup = await guest.GetAsync(new Uri($"/ujian/{second}/mulai", UriKind.Relative));
        using var post = await guest.PostFormAsync("/akun/masuk", PracticeForm(), $"/ujian/{second}/mulai");
        var examPage = WebUtility.HtmlDecode(await guest.GetStringAsync(new Uri($"/ujian/{second}", UriKind.Relative)));
        using var stillOpen = await guest.GetAsync(new Uri(player, UriKind.Relative));

        Assert.Equal((HttpStatusCode.Redirect, $"/ujian/{second}"), (setup.StatusCode, setup.Headers.Location!.OriginalString));
        Assert.Equal((HttpStatusCode.Redirect, $"/ujian/{second}"), (post.StatusCode, post.Headers.Location!.OriginalString));
        Assert.Contains("data-testid=\"cta-register-continue\"", examPage, StringComparison.Ordinal);
        Assert.Contains("Anda sudah memakai tes gratis tanpa akun", examPage, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"cta-trial\"", examPage, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, stillOpen.StatusCode);
        await using var db = database.CreateContext();
        Assert.Equal(1, await db.Attempts.CountAsync(a => a.ExamId == first || a.ExamId == second));
    }

    [Fact]
    public async Task Registered_learners_keep_taking_trials_on_every_exam()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (first, second) = await TwoPaidExamsAsync();

        await StartAsync(learner, first);
        await StartAsync(learner, first);
        await StartAsync(learner, second);
    }

    [Fact]
    public async Task The_guest_result_invites_them_to_register_and_registering_carries_the_trial_with_its_cards()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (examId, _) = await TwoPaidExamsAsync();
        var player = await StartAsync(guest, examId);
        await AnswerFirstRightAndSubmitAsync(guest, player);

        var result = WebUtility.HtmlDecode(await guest.GetStringAsync(new Uri($"{player}/hasil", UriKind.Relative)));
        var email = $"tamu-{TestData.Unique()}@akusiap.test";
        using var registered = await guest.PostFormAsync("/akun/daftar", new Dictionary<string, string>
        {
            ["Input.Email"] = email, ["Input.DisplayName"] = "Tamu", ["Input.Password"] = Password, ["ReturnUrl"] = "/learn/analisis",
        });
        var analysis = WebUtility.HtmlDecode(await guest.GetStringAsync(new Uri("/learn/analisis", UriKind.Relative)));

        Assert.Contains("data-testid=\"guest-trial-invite\"", result, StringComparison.Ordinal);
        Assert.Contains("2 soal yang salah jadi kartu hafalan", result, StringComparison.Ordinal);
        Assert.Contains("href=\"/akun/daftar?ReturnUrl=%2Flearn%2Fanalisis\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"retake-wrong\"", result, StringComparison.Ordinal);
        Assert.True(result.IndexOf("guest-trial-invite", StringComparison.Ordinal) < result.IndexOf("trial-offer", StringComparison.Ordinal), "The invite comes before the buy offer");
        Assert.Equal((HttpStatusCode.Redirect, "/learn/analisis"), (registered.StatusCode, registered.Headers.Location!.OriginalString));
        await AssertClaimedAsync(email, examId, expectedCards: 2);
        Assert.Contains($"data-exam=\"{examId}\"", analysis, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"analysis-weakest-topic\"", analysis, StringComparison.Ordinal);
        Assert.Equal(["PractiseTopic", "Unlock", "SetGoal"], StepKinds(analysis)); // a trial without a goal has no card deck
    }

    [Fact]
    public async Task Signing_in_to_an_existing_account_also_carries_the_trial_over()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (examId, _) = await TwoPaidExamsAsync();
        await AnswerFirstRightAndSubmitAsync(guest, await StartAsync(guest, examId));

        using var signedIn = await guest.PostFormAsync("/akun/masuk", new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email!, ["Input.Password"] = Password, ["ReturnUrl"] = "/learn/analisis",
        });

        Assert.Equal((HttpStatusCode.Redirect, "/learn/analisis"), (signedIn.StatusCode, signedIn.Headers.Location!.OriginalString));
        await AssertClaimedAsync(user.Email!, examId, expectedCards: 2);
    }

    [Fact]
    public async Task Signed_in_learners_see_no_guest_invite()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var (examId, _) = await TwoPaidExamsAsync();
        var player = await StartAsync(learner, examId);
        await AnswerFirstRightAndSubmitAsync(learner, player);

        var result = await learner.GetStringAsync(new Uri($"{player}/hasil", UriKind.Relative));

        Assert.DoesNotContain("data-testid=\"guest-trial-invite\"", result, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"retake-wrong\"", result, StringComparison.Ordinal);
    }

    private async Task AssertClaimedAsync(string email, long examId, int expectedCards)
    {
        await using var db = database.CreateContext();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var attempt = await db.Attempts.SingleAsync(a => a.ExamId == examId);
        Assert.Equal((userId, (Guid?)null), (attempt.UserId!.Value, attempt.GuestToken));
        var cards = await db.ReviewCards.Where(c => c.UserId == userId).ToListAsync();
        Assert.Equal(expectedCards, cards.Count);
        Assert.All(cards, c => Assert.Equal(ReviewCardState.Learning, c.State));
    }

    private async Task<(long First, long Second)> TwoPaidExamsAsync()
    {
        await using var db = database.CreateContext();
        var first = await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(4, trial: 3).Build());
        var second = await db.SaveAsync(TestData.Exam().Paid(59_000).WithQuestions(4, trial: 3).Build());
        return (first.Id, second.Id);
    }

    private static Dictionary<string, string> PracticeForm() => new()
    {
        ["Form.Mode"] = "Practice", ["Form.Untimed"] = "true", ["Form.ShuffleQuestions"] = "false", ["Form.ShuffleOptions"] = "false",
    };

    private static async Task<string> StartAsync(HttpClient client, long examId)
    {
        using var response = await client.PostFormAsync($"/ujian/{examId}/mulai", PracticeForm());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/sesi/", location, StringComparison.Ordinal);
        return location;
    }

    /// <summary>Answers the first question right, leaves the rest blank (wrong) and submits.</summary>
    private static async Task AnswerFirstRightAndSubmitAsync(HttpClient client, string player)
    {
        var html = await client.GetStringAsync(new Uri(player, UriKind.Relative));
        var state = JsonDocument.Parse(StatePattern().Match(html).Groups[1].Value).RootElement;
        var first = state.GetProperty("items")[0].GetProperty("options")[0].GetProperty("id").GetInt64();
        using (var save = new HttpRequestMessage(HttpMethod.Post, new Uri($"{player}/jawab", UriKind.Relative))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["position"] = "1", ["selected"] = first.ToString(System.Globalization.CultureInfo.InvariantCulture), ["flagged"] = "false", ["seconds"] = "4",
            }),
        })
        {
            save.Headers.Add("RequestVerificationToken", state.GetProperty("token").GetString());
            using var saved = await client.SendAsync(save);
            Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        }

        using var submitted = await client.PostFormAsync(player, new Dictionary<string, string>(), $"{player}?handler=Submit");
        Assert.Equal(HttpStatusCode.Redirect, submitted.StatusCode);
    }

    private static List<string> StepKinds(string html) => [.. StepPattern().Matches(html).Select(m => m.Groups[1].Value)];

    [GeneratedRegex("<script type=\"application/json\" id=\"quiz-state\">(.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex StatePattern();

    [GeneratedRegex("data-testid=\"analysis-step\" data-kind=\"(\\w+)\"")]
    private static partial Regex StepPattern();
}
