using System.Net;
using System.Security.Claims;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Akusiap.Web.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Accounts;

/// <summary>AC-03: the Google button follows the configuration, and Google logins link to or create accounts.</summary>
[Collection(UsesDatabase.Name)]
public sealed class GoogleSignInTests(PostgresFixture database)
{
    [Fact]
    public async Task Without_config_the_button_is_hidden_and_the_app_still_starts()
    {
        await using var app = new AppFactory(database);
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var signIn = await client.GetStringAsync(new Uri("/akun/masuk", UriKind.Relative));
        var register = await client.GetStringAsync(new Uri("/akun/daftar", UriKind.Relative));
        using var challenge = await client.PostFormAsync("/akun/masuk", new Dictionary<string, string>(), "/akun/google");

        Assert.DoesNotContain("data-testid=\"google-sign-in\"", signIn, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"google-sign-in\"", register, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, challenge.StatusCode);
    }

    [Fact]
    public async Task With_config_the_button_appears_and_sends_the_user_to_google()
    {
        await using var app = new AppFactory(database);
        await using var google = app.WithWebHostBuilder(b => b
            .UseSetting("Authentication:Google:ClientId", "test-client-id")
            .UseSetting("Authentication:Google:ClientSecret", "test-secret"));
        using var client = google.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var signIn = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/akun/masuk?ReturnUrl=%2Fujian%2F5", UriKind.Relative)));
        var register = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/akun/daftar", UriKind.Relative)));
        using var challenge = await client.PostFormAsync("/akun/masuk", new Dictionary<string, string> { ["returnUrl"] = "/ujian/5" }, "/akun/google");

        Assert.Contains("Masuk dengan Google", signIn, StringComparison.Ordinal);
        Assert.Contains("name=\"returnUrl\" value=\"/ujian/5\"", signIn, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"google-sign-in\"", register, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var location = challenge.Headers.Location!.ToString();
        Assert.StartsWith("https://accounts.google.com/", location, StringComparison.Ordinal);
        Assert.Contains("client_id=test-client-id", location, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("/signin-google"), location, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_new_google_user_gets_a_confirmed_learner_account_without_a_password()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var email = $"google-{TestData.Unique()}@gmail.com";
        var key = TestData.Unique();

        var (outcome, user) = await FindOrCreateAsync(app, Login(key, email, "Sari Google"));
        var (again, same) = await FindOrCreateAsync(app, Login(key, "other@example.com", "Sari"));

        Assert.Equal(GoogleAccountOutcome.SignedIn, outcome);
        Assert.Equal((GoogleAccountOutcome.SignedIn, user!.Id), (again, same!.Id));
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var saved = await users.FindByEmailAsync(email);
        Assert.NotNull(saved);
        Assert.True(saved.EmailConfirmed);
        Assert.Equal("Sari Google", saved.DisplayName);
        Assert.False(await users.HasPasswordAsync(saved));
        Assert.Equal([RoleNames.Learner], await users.GetRolesAsync(saved));
    }

    [Fact]
    public async Task An_existing_account_is_linked_by_email_and_its_email_confirmed()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var existing = await app.CreateUserAsync(TestData.User().Unconfirmed().Build(), "kopi-susu-2026", RoleNames.Learner);

        var (outcome, user) = await FindOrCreateAsync(app, Login(TestData.Unique(), existing.Email!.ToUpperInvariant(), "Nama Google"));

        Assert.Equal((GoogleAccountOutcome.SignedIn, existing.Id), (outcome, user!.Id));
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var saved = await users.FindByIdAsync(existing.Id.ToString());
        Assert.True(saved!.EmailConfirmed);
        Assert.Equal(existing.DisplayName, saved.DisplayName);
        Assert.Single(await users.GetLoginsAsync(saved));
        Assert.True(await users.HasPasswordAsync(saved));
    }

    [Fact]
    public async Task Deactivated_accounts_and_logins_without_an_email_are_refused()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var deactivated = await app.CreateUserAsync(TestData.User().Deactivated().Build(), "kopi-susu-2026", RoleNames.Learner);

        var (blocked, _) = await FindOrCreateAsync(app, Login(TestData.Unique(), deactivated.Email!, "X"));
        var (noEmail, _) = await FindOrCreateAsync(app, Login(TestData.Unique(), null, "X"));

        Assert.Equal(GoogleAccountOutcome.Deactivated, blocked);
        Assert.Equal(GoogleAccountOutcome.NoEmail, noEmail);
    }

    private static async Task<(GoogleAccountOutcome Outcome, User? User)> FindOrCreateAsync(AppFactory app, ExternalLoginInfo login)
    {
        await using var scope = app.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<GoogleAccounts>().FindOrCreateAsync(login);
    }

    private static ExternalLoginInfo Login(string key, string? email, string name)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, key), new(ClaimTypes.Name, name) };
        if (email is not null)
        {
            claims.Add(new Claim(ClaimTypes.Email, email));
        }

        return new ExternalLoginInfo(new ClaimsPrincipal(new ClaimsIdentity(claims, "Google")), GoogleSignIn.Scheme, key, "Google");
    }
}
