using System.Net;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Accounts;

/// <summary>AC-04: the profile page, the teacher switch and changing the password.</summary>
[Collection(UsesDatabase.Name)]
public sealed class ProfileTests(PostgresFixture database)
{
    private const string TestPassword = "test-sandi-2026"; // CreateSignedInClientAsync's password

    [Fact]
    public async Task Saving_the_profile_stores_a_normalized_phone_and_switches_the_language_at_once()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);

        using var saved = await client.PostFormAsync("/learn/profil", Profile("Rina Baru", "0812 3456 7890", Languages.English), "/learn/profil?handler=Profile");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/learn/profil", UriKind.Relative)));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("<html lang=\"en\"", html, StringComparison.Ordinal);
        Assert.Contains("Profile saved.", html, StringComparison.Ordinal);
        Assert.Contains(">Rina Baru</a>", html, StringComparison.Ordinal);
        var stored = await LoadAsync(user.Id);
        Assert.Equal(("Rina Baru", "+6281234567890", Languages.English), (stored.DisplayName, stored.PhoneNumber, stored.PreferredLanguage));
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("+65 8123 4567")]
    [InlineData("+62 812")]
    public async Task Invalid_phone_numbers_are_refused_and_nothing_is_saved(string phone)
    {
        await using var app = await StartAsync();
        var user = TestData.User().Named("Rina").Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);

        using var response = await client.PostFormAsync("/learn/profil", Profile("Nama Lain", phone, Languages.Indonesian), "/learn/profil?handler=Profile");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Nomor HP harus +62 diikuti 8–12 angka.", html, StringComparison.Ordinal);
        Assert.Equal("Rina", (await LoadAsync(user.Id)).DisplayName);
    }

    [Fact]
    public async Task An_empty_phone_clears_the_number()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        user.PhoneNumber = "+6281234567890";
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);

        using (await client.PostFormAsync("/learn/profil", Profile("Rina", string.Empty, Languages.Indonesian), "/learn/profil?handler=Profile"))
        {
        }

        Assert.Null((await LoadAsync(user.Id)).PhoneNumber);
    }

    [Fact]
    public async Task The_teacher_switch_adds_and_removes_the_teacher_role()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);
        var form = Profile("Rina", string.Empty, Languages.Indonesian);
        form["Input.IsTeacher"] = "true";

        using (await client.PostFormAsync("/learn/profil", form, "/learn/profil?handler=Profile"))
        {
        }

        Assert.Equal([RoleNames.Learner, RoleNames.Teacher], (await RolesAsync(user.Id)).Order());
        Assert.Matches("<input(?=[^>]*name=\"Input.IsTeacher\")(?=[^>]*checked)[^>]*>", await client.GetStringAsync(new Uri("/learn/profil", UriKind.Relative)));

        form["Input.IsTeacher"] = "false";
        using (await client.PostFormAsync("/learn/profil", form, "/learn/profil?handler=Profile"))
        {
        }

        Assert.Equal([RoleNames.Learner], await RolesAsync(user.Id));
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_and_keeps_the_user_signed_in()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);

        using var wrong = await client.PostFormAsync("/learn/profil", PasswordForm("salah-sandi-1", "baru-sandi-2027"), "/learn/profil?handler=Password");
        var wrongHtml = WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync());
        using var changed = await client.PostFormAsync("/learn/profil", PasswordForm(TestPassword, "baru-sandi-2027"), "/learn/profil?handler=Password");
        var after = WebUtility.HtmlDecode(await client.GetStringAsync(new Uri("/learn/profil", UriKind.Relative)));

        Assert.Contains("Kata sandi sekarang salah.", wrongHtml, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        Assert.Contains("Kata sandi diubah.", after, StringComparison.Ordinal);

        using var fresh = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var oldSignIn = await fresh.PostFormAsync("/akun/masuk", SignIn(user.Email!, TestPassword));
        using var newSignIn = await fresh.PostFormAsync("/akun/masuk", SignIn(user.Email!, "baru-sandi-2027"));
        Assert.Equal(HttpStatusCode.OK, oldSignIn.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, newSignIn.StatusCode);
    }

    [Fact]
    public async Task A_short_new_password_is_refused()
    {
        await using var app = await StartAsync();
        var user = TestData.User().Build();
        using var client = await app.CreateSignedInClientAsync(user, RoleNames.Learner);

        using var response = await client.PostFormAsync("/learn/profil", PasswordForm(TestPassword, "pendek"), "/learn/profil?handler=Password");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var stillOld = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var signIn = await stillOld.PostFormAsync("/akun/masuk", SignIn(user.Email!, TestPassword));
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
    }

    [Fact]
    public async Task Guests_are_sent_to_sign_in()
    {
        await using var app = await StartAsync();
        using var guest = app.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var response = await guest.GetAsync(new Uri("/learn/profil", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private static Dictionary<string, string> Profile(string name, string phone, string language) => new()
    {
        ["Input.DisplayName"] = name,
        ["Input.PhoneNumber"] = phone,
        ["Input.PreferredLanguage"] = language,
        ["Input.IsTeacher"] = "false",
    };

    private static Dictionary<string, string> PasswordForm(string current, string next) => new()
    {
        ["Password.Current"] = current,
        ["Password.New"] = next,
    };

    private static Dictionary<string, string> SignIn(string email, string password) => new()
    {
        ["Input.Email"] = email,
        ["Input.Password"] = password,
    };

    private async Task<User> LoadAsync(Guid id)
    {
        await using var db = database.CreateContext();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == id);
    }

    private async Task<List<string>> RolesAsync(Guid id)
    {
        await using var db = database.CreateContext();
        return await db.UserRoles.Where(r => r.UserId == id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync();
    }
}
