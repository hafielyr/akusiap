using System.Net;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Accounts;

/// <summary>AC-01: register, sign in and sign out with email.</summary>
[Collection(UsesDatabase.Name)]
public sealed class AccountTests(PostgresFixture database)
{
    private const string Password = "kopi-susu-2026";
    private const string AuthCookie = ".AspNetCore.Identity.Application";

    [Fact]
    public async Task Guest_registers_and_lands_on_the_catalog_signed_in_as_a_learner()
    {
        await using var app = await StartAsync();
        using var client = NewClient(app);
        var email = $"rina-{TestData.Unique()}@example.com";

        using var response = await client.PostFormAsync("/akun/daftar", RegisterForm(email, "Rina Wati", Password));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/ujian", response.Headers.Location?.OriginalString);
        Assert.True(SetsCookie(response, AuthCookie), "the sign-in cookie is set");
        var catalog = await client.GetStringAsync(new Uri("/ujian", UriKind.Relative));
        Assert.Contains("Katalog ujian", catalog, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"signed-in-name\">Rina Wati<", catalog, StringComparison.Ordinal);

        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await users.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.Equal(["Learner"], await users.GetRolesAsync(user));
        Assert.Equal(Languages.Indonesian, user.PreferredLanguage);
        Assert.Equal(app.Clock.UtcNow, user.CreatedAt);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task Registering_an_existing_email_in_another_letter_case_is_refused()
    {
        await using var app = await StartAsync();
        var existing = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        using var client = NewClient(app);

        using var response = await client.PostFormAsync("/akun/daftar", RegisterForm(existing.Email!.ToUpperInvariant(), "Kembar", Password));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("sudah terdaftar", html, StringComparison.Ordinal);
        Assert.False(SetsCookie(response, AuthCookie));
    }

    [Fact]
    public async Task Short_passwords_are_refused_in_indonesian()
    {
        await using var app = await StartAsync();
        using var client = NewClient(app);

        using var response = await client.PostFormAsync("/akun/daftar", RegisterForm($"pendek-{TestData.Unique()}@example.com", "Pendek", "1234567"));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Kata sandi minimal 8 karakter.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_signs_in_with_the_right_password_and_not_with_a_wrong_one()
    {
        await using var app = await StartAsync();
        var user = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        using var client = NewClient(app);

        using var wrong = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, "salah-total"));
        using var right = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, Password));

        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("Email atau kata sandi salah.", await wrong.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        Assert.Equal("/ujian", right.Headers.Location?.OriginalString);
        Assert.True(SetsCookie(right, AuthCookie));
    }

    [Fact]
    public async Task Deactivated_user_sees_account_deactivated_and_is_not_signed_in()
    {
        await using var app = await StartAsync();
        var user = await app.CreateUserAsync(TestData.User().Deactivated().Build(), Password, RoleNames.Learner);
        using var client = NewClient(app);

        using var withRightPassword = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, Password));
        using var withWrongPassword = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, "bukan-ini-ya"));

        Assert.Equal(HttpStatusCode.OK, withRightPassword.StatusCode);
        Assert.Contains("Akun dinonaktifkan", await withRightPassword.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(SetsCookie(withRightPassword, AuthCookie));
        // Without the right password the page doesn't reveal that the account exists or is deactivated.
        Assert.Contains("Email atau kata sandi salah.", await withWrongPassword.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_for_a_while()
    {
        await using var app = await StartAsync();
        var user = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        using var client = NewClient(app);

        for (var i = 0; i < 5; i++)
        {
            using var _ = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, $"salah-{i}-sandi"));
        }

        using var response = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, Password));

        Assert.Contains("Terlalu banyak percobaan", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(SetsCookie(response, AuthCookie));
    }

    [Fact]
    public async Task Sign_out_clears_the_sign_in_cookie()
    {
        await using var app = await StartAsync();
        var user = await app.CreateUserAsync(TestData.User().Named("Budi").Build(), Password, RoleNames.Learner);
        using var client = NewClient(app);
        using var signedIn = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, Password));

        using var signedOut = await client.PostFormAsync("/", new Dictionary<string, string>(), postUrl: "/akun/keluar");
        var home = await client.GetStringAsync(new Uri("/", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
        Assert.Contains(signedOut.Headers.GetValues("Set-Cookie"), c => c.StartsWith(AuthCookie + "=;", StringComparison.Ordinal));
        Assert.DoesNotContain("signed-in-name", home, StringComparison.Ordinal);
        Assert.Contains("href=\"/akun/masuk\"", home, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/bahasa/id-ID", "/bahasa/id-ID")]
    [InlineData("https://evil.example/", "/ujian")]
    [InlineData("//evil.example", "/ujian")]
    public async Task Only_local_return_urls_are_followed(string returnUrl, string expected)
    {
        await using var app = await StartAsync();
        var user = await app.CreateUserAsync(TestData.User().Build(), Password, RoleNames.Learner);
        using var client = NewClient(app);
        var page = "/akun/masuk?returnUrl=" + Uri.EscapeDataString(returnUrl);

        using var response = await client.PostFormAsync(page, SignInForm(user.Email!, Password), postUrl: page);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(expected, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Sign_in_switches_the_ui_to_the_users_language()
    {
        await using var app = await StartAsync();
        var user = await app.CreateUserAsync(TestData.User().English().Build(), Password, RoleNames.Learner);
        using var client = NewClient(app);

        using var response = await client.PostFormAsync("/akun/masuk", SignInForm(user.Email!, Password));
        var catalog = await client.GetStringAsync(new Uri("/ujian", UriKind.Relative));

        Assert.True(SetsCookie(response, ".AspNetCore.Culture"));
        Assert.Contains("Exam catalog", catalog, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seeded_admin_signs_in_through_the_sign_in_page()
    {
        var adminEmail = $"admin-{TestData.Unique()}@akusiap.test";
        await using var app = new AppFactory(database).WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Seed:AdminEmail", adminEmail);
            builder.UseSetting("Seed:AdminPassword", Password);
        });
        await app.SeedReferenceDataAsync();
        using var client = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await client.PostFormAsync("/akun/masuk", SignInForm(adminEmail, Password));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.True(SetsCookie(response, AuthCookie));
    }

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private static HttpClient NewClient(WebApplicationFactory<Program> app) =>
        app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static Dictionary<string, string> RegisterForm(string email, string displayName, string password) => new()
    {
        ["Input.Email"] = email,
        ["Input.DisplayName"] = displayName,
        ["Input.Password"] = password,
    };

    private static Dictionary<string, string> SignInForm(string email, string password) => new()
    {
        ["Input.Email"] = email,
        ["Input.Password"] = password,
        ["Input.RememberMe"] = "true",
    };

    private static bool SetsCookie(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
        && cookies.Any(c => c.StartsWith(name + "=", StringComparison.Ordinal) && !c.StartsWith(name + "=;", StringComparison.Ordinal));
}
