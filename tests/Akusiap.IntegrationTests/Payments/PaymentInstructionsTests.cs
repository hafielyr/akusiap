using System.Net;
using Akusiap.Application.Payments;
using Akusiap.Domain.Payments;
using Akusiap.Domain.Users;
using Akusiap.Infrastructure.Payments;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Payments;

/// <summary>PAY-05: payment instructions, the status poll and the pending list.</summary>
[Collection(UsesDatabase.Name)]
public sealed class PaymentInstructionsTests(PostgresFixture database)
{
    [Fact]
    public async Task Qris_shows_a_qr_and_countdown_and_the_poll_refreshes_once_paid()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var user = TestData.User().Build();
        using var learner = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var examId = await ExamAsync();
        var order = await CheckoutAsync(learner, examId, "QRIS");

        var page = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"/bayar/{order}", UriKind.Relative)));
        using var pendingPoll = await learner.GetAsync(new Uri($"/bayar/{order}?handler=Status&shown=Pending", UriKind.Relative));
        await PayAsync(app, order);
        using var paidPoll = await learner.GetAsync(new Uri($"/bayar/{order}?handler=Status&shown=Pending", UriKind.Relative));
        var paidPage = await learner.GetStringAsync(new Uri($"/bayar/{order}", UriKind.Relative));

        Assert.Contains("data-testid=\"pay-qr\"", page, StringComparison.Ordinal);
        Assert.Contains("<svg", page, StringComparison.Ordinal);
        Assert.Contains("left: 86400", page, StringComparison.Ordinal);
        Assert.Contains("Rp49.000", page, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NoContent, pendingPoll.StatusCode);
        Assert.False(pendingPoll.Headers.Contains("HX-Refresh"));
        Assert.Equal("true", paidPoll.Headers.GetValues("HX-Refresh").Single());
        Assert.Contains("data-testid=\"pay-success\"", paidPage, StringComparison.Ordinal);
        Assert.Contains($"href=\"/ujian/{examId}/mulai\"", paidPage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Virtual_accounts_show_the_number_with_bank_steps_and_returning_shows_the_same()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var order = await CheckoutAsync(learner, await ExamAsync(), "VA-BCA");

        var first = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"/bayar/{order}", UriKind.Relative)));
        var later = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"/bayar/{order}", UriKind.Relative)));
        var list = await learner.GetStringAsync(new Uri("/learn/pembayaran", UriKind.Relative));

        Assert.Matches("data-testid=\"va-number\">8808[0-9]{12}<", first);
        Assert.Contains("Buka aplikasi mobile banking BCA.", first, StringComparison.Ordinal);
        Assert.Equal(System.Text.RegularExpressions.Regex.Match(first, "8808[0-9]{12}").Value, System.Text.RegularExpressions.Regex.Match(later, "8808[0-9]{12}").Value);
        Assert.Contains($"data-order=\"{order}\"", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Past_the_deadline_the_payment_shows_expired_and_leaves_the_pending_list()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        var order = await CheckoutAsync(learner, await ExamAsync(), "QRIS");
        app.Clock.Advance(TimeSpan.FromHours(25));

        var page = WebUtility.HtmlDecode(await learner.GetStringAsync(new Uri($"/bayar/{order}", UriKind.Relative)));
        using var poll = await learner.GetAsync(new Uri($"/bayar/{order}?handler=Status&shown=Pending", UriKind.Relative));
        var list = await learner.GetStringAsync(new Uri("/learn/pembayaran", UriKind.Relative));

        Assert.Contains("Waktu pembayaran habis.", page, StringComparison.Ordinal);
        Assert.Equal("true", poll.Headers.GetValues("HX-Refresh").Single());
        Assert.Contains("data-testid=\"no-pending\"", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Only_the_payer_can_see_the_instructions()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var other = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var order = await CheckoutAsync(learner, await ExamAsync(), "QRIS");

        using var forOther = await other.GetAsync(new Uri($"/bayar/{order}", UriKind.Relative));
        using var forGuest = await guest.GetAsync(new Uri($"/bayar/{order}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, forOther.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, forGuest.StatusCode);
    }

    private async Task<long> ExamAsync()
    {
        await using var db = database.CreateContext();
        return (await db.SaveAsync(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1).Build())).Id;
    }

    private static async Task<string> CheckoutAsync(HttpClient client, long examId, string channel)
    {
        using var placed = await client.PostFormAsync($"/checkout?exam={examId}", new Dictionary<string, string> { ["Channel"] = channel, ["AcceptTerms"] = "true" });
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        return placed.Headers.Location!.OriginalString["/bayar/".Length..];
    }

    private static async Task PayAsync(AppFactory app, string order)
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ApplyGatewayNotification>()
            .HandleAsync(FakePaymentGateway.CreateNotification(order, "PAID", 49_000), CancellationToken.None);
    }
}
