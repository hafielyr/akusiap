using System.Net;
using System.Text.RegularExpressions;
using Akusiap.Application.Abstractions;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Accounts;

/// <summary>AC-02: the confirmation email on registration, confirming, resending, and the forgot/reset password flow.</summary>
[Collection(UsesDatabase.Name)]
public sealed partial class EmailFlowTests(PostgresFixture database)
{
    private const string Password = "kopi-susu-2026";

    [Fact]
    public async Task Registering_sends_a_confirmation_email_whose_link_confirms()
    {
        await using var app = await StartAsync();
        using var client = NewClient(app);
        var email = NewEmail();

        using var registered = await client.PostFormAsync("/akun/daftar", RegisterForm(email));

        Assert.Equal(HttpStatusCode.Redirect, registered.StatusCode);
        var message = Assert.Single(app.Email.To(email));
        Assert.Equal("Konfirmasi email Akusiap Anda", message.Subject);
        Assert.Contains("Halo Rina,", message.TextBody, StringComparison.Ordinal);
        Assert.False(await ConfirmedAsync(email));

        var html = await client.GetStringAsync(new Uri(LinkIn(message), UriKind.Relative));

        Assert.Contains("data-testid=\"confirm-done\"", html, StringComparison.Ordinal);
        Assert.True(await ConfirmedAsync(email));
    }

    [Fact]
    public async Task A_tampered_confirmation_link_does_not_confirm()
    {
        await using var app = await StartAsync();
        using var client = NewClient(app);
        var email = NewEmail();
        using (await client.PostFormAsync("/akun/daftar", RegisterForm(email)))
        {
        }

        var link = LinkIn(app.Email.To(email)[0]);
        var html = await client.GetStringAsync(new Uri(link[..^4] + "AAAA", UriKind.Relative));
        var garbage = await client.GetStringAsync(new Uri("/akun/konfirmasi?userId=not-a-guid&code=xyz", UriKind.Relative));

        Assert.Contains("data-testid=\"confirm-failed\"", html, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"confirm-failed\"", garbage, StringComparison.Ordinal);
        Assert.False(await ConfirmedAsync(email));
    }

    [Fact]
    public async Task Unconfirmed_users_can_ask_for_the_email_again_from_the_learn_page()
    {
        await using var app = await StartAsync();
        using var client = NewClient(app);
        var email = NewEmail();
        using (await client.PostFormAsync("/akun/daftar", RegisterForm(email)))
        {
        }

        var before = await client.GetStringAsync(new Uri("/learn", UriKind.Relative));
        using var resent = await client.PostFormAsync("/learn", new Dictionary<string, string>(), "/learn?handler=ResendConfirmation");
        var after = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/learn", UriKind.Relative)));

        Assert.Contains("data-testid=\"confirm-pending\"", before, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, resent.StatusCode);
        Assert.Equal(2, app.Email.To(email).Count);
        Assert.Contains("Email konfirmasi sudah dikirim.", after, StringComparison.Ordinal);

        await client.GetStringAsync(new Uri(LinkIn(app.Email.To(email)[1]), UriKind.Relative));
        var confirmed = await client.GetStringAsync(new Uri("/learn", UriKind.Relative));

        Assert.DoesNotContain("data-testid=\"confirm-pending\"", confirmed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reset_link_changes_the_password_confirms_the_email_and_clears_a_lockout()
    {
        await using var app = await StartAsync();
        var user = await app.CreateUserAsync(TestData.User().Unconfirmed().Build(), Password, RoleNames.Learner);
        await using (var db = database.CreateContext())
        {
            var saved = await db.Users.SingleAsync(u => u.Id == user.Id);
            saved.AccessFailedCount = 3;
            saved.LockoutEnd = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero); // Identity checks lockout against the real clock
            await db.SaveChangesAsync();
        }

        using var client = NewClient(app);

        var sent = await client.PostFormAsync("/akun/lupa-sandi", new Dictionary<string, string> { ["Input.Email"] = user.Email!.ToUpperInvariant() });
        var sentHtml = WebUtility.HtmlDecode(await sent.Content.ReadAsStringAsync());
        sent.Dispose();
        var message = Assert.Single(app.Email.To(user.Email!));
        Assert.Equal("Atur ulang kata sandi Akusiap", message.Subject);
        Assert.Contains("Jika email itu terdaftar", sentHtml, StringComparison.Ordinal);

        var link = LinkIn(message);
        using var reset = await client.PostFormAsync(link, new Dictionary<string, string>
        {
            ["Input.Email"] = user.Email!,
            ["Input.Code"] = Query(link, "code"),
            ["Input.Password"] = "teh-tarik-2027",
        }, "/akun/atur-ulang-sandi");

        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Equal("/akun/masuk?reset=true", reset.Headers.Location?.OriginalString);
        Assert.Contains("data-testid=\"reset-done\"", await client.GetStringAsync(new Uri("/akun/masuk?reset=true", UriKind.Relative)), StringComparison.Ordinal);
        using var oldPassword = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, Password));
        using var newPassword = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, "teh-tarik-2027"));
        Assert.Equal(HttpStatusCode.OK, oldPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, newPassword.StatusCode);
        Assert.True(await ConfirmedAsync(user.Email!));
    }

    [Fact]
    public async Task A_reset_link_works_only_once()
    {
        await using var app = await StartAsync();
        var user = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        using var client = NewClient(app);
        using (await client.PostFormAsync("/akun/lupa-sandi", new Dictionary<string, string> { ["Input.Email"] = user.Email! }))
        {
        }

        var link = LinkIn(app.Email.To(user.Email!)[0]);
        var form = new Dictionary<string, string> { ["Input.Email"] = user.Email!, ["Input.Code"] = Query(link, "code"), ["Input.Password"] = "teh-tarik-2027" };
        using (await client.PostFormAsync(link, form, "/akun/atur-ulang-sandi"))
        {
        }

        form["Input.Password"] = "es-jeruk-2028";
        using var again = await client.PostFormAsync(link, form, "/akun/atur-ulang-sandi");

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Contains("data-testid=\"reset-invalid\"", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Forgot_password_says_the_same_for_unknown_and_deactivated_emails_and_sends_nothing()
    {
        await using var app = await StartAsync();
        var deactivated = await app.CreateUserAsync(TestData.User().Deactivated().Build(), Password, RoleNames.Learner);
        using var client = NewClient(app);
        var unknown = NewEmail();

        using var forUnknown = await client.PostFormAsync("/akun/lupa-sandi", new Dictionary<string, string> { ["Input.Email"] = unknown });
        using var forDeactivated = await client.PostFormAsync("/akun/lupa-sandi", new Dictionary<string, string> { ["Input.Email"] = deactivated.Email! });

        Assert.Contains("data-testid=\"forgot-sent\"", await forUnknown.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Contains("data-testid=\"forgot-sent\"", await forDeactivated.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(app.Email.To(unknown));
        Assert.Empty(app.Email.To(deactivated.Email!));
    }

    [Fact]
    public async Task Email_links_use_the_configured_site_address_not_the_request_host()
    {
        await using var app = await StartAsync();
        await using var configured = app.WithWebHostBuilder(b => b.UseSetting("Site:BaseUrl", "https://akusiap.test/"));
        using var client = configured.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Host = "evil.example";
        var email = NewEmail();

        using (await client.PostFormAsync("/akun/daftar", RegisterForm(email)))
        {
        }

        var message = Assert.Single(app.Email.To(email));
        Assert.Contains("https://akusiap.test/akun/konfirmasi?userId=", message.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", message.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sign_in_page_links_to_forgot_password()
    {
        await using var app = await StartAsync();
        using var client = NewClient(app);

        var html = await client.GetStringAsync(new Uri("/akun/masuk", UriKind.Relative));

        Assert.Contains("href=\"/akun/lupa-sandi\"", html, StringComparison.Ordinal);
    }

    // ---------- Helpers ----------

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string NewEmail() => $"rina-{TestData.Unique()}@example.com";

    private static Dictionary<string, string> RegisterForm(string email) => new()
    {
        ["Input.Email"] = email,
        ["Input.DisplayName"] = "Rina",
        ["Input.Password"] = Password,
    };

    private static Dictionary<string, string> SignInForm(string email, string password) => new()
    {
        ["Input.Email"] = email,
        ["Input.Password"] = password,
    };

    /// <summary>The path and query of the link in the plain-text body.</summary>
    private static string LinkIn(EmailMessage message)
    {
        var match = LinkPattern().Match(message.TextBody ?? string.Empty);
        Assert.True(match.Success, "No link in the email");
        return new Uri(match.Value).PathAndQuery;
    }

    private static string Query(string pathAndQuery, string name) =>
        Uri.UnescapeDataString(Regex.Match(pathAndQuery, $"[?&]{name}=([^&]+)").Groups[1].Value);

    private async Task<bool> ConfirmedAsync(string email)
    {
        var normalized = email.ToUpperInvariant();
        await using var db = database.CreateContext();
        return await db.Users.Where(u => u.NormalizedEmail == normalized).Select(u => u.EmailConfirmed).SingleAsync();
    }

    [GeneratedRegex("https?://\\S+")]
    private static partial Regex LinkPattern();
}
