using System.Globalization;
using System.Net;
using Akusiap.Domain.Content;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;

namespace Akusiap.IntegrationTests.Admin;

/// <summary>ADM-T5: system rule presets, exactly one default, and delete rules.</summary>
[Collection(UsesDatabase.Name)]
public sealed class RulePresetTests(PostgresFixture database)
{
    [Fact]
    public async Task Admin_creates_a_preset_with_every_setting()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var name = $"Simulasi {TestData.Unique()}";

        using var response = await admin.PostFormAsync("/admin/aturan/baru",
            Form(name, time: "100", count: "110", attempts: "3", pass: "65", show: ShowAnswers.Never, hints: false, shuffleQuestions: true, shuffleOptions: false));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = database.CreateContext();
        var preset = await db.RulePresets.SingleAsync(r => r.Name == name);
        Assert.Equal(((int?)100, (int?)110, (int?)3, (int?)65), (preset.TimeLimitMinutes, preset.QuestionCount, preset.MaxAttempts, preset.PassMarkPercent));
        Assert.Equal((false, true, false, ShowAnswers.Never, false), (preset.HintsEnabled, preset.ShuffleQuestions, preset.ShuffleOptions, preset.ShowAnswers, preset.IsDefault));
        Assert.Null(preset.OwnerUserId);
    }

    [Fact]
    public async Task Blank_limits_are_saved_as_no_limit()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var name = $"Bebas {TestData.Unique()}";

        using var response = await admin.PostFormAsync("/admin/aturan/baru", Form(name));

        await using var db = database.CreateContext();
        var preset = await db.RulePresets.SingleAsync(r => r.Name == name);
        Assert.Equal(((int?)null, (int?)null, (int?)null, (int?)null), (preset.TimeLimitMinutes, preset.QuestionCount, preset.MaxAttempts, preset.PassMarkPercent));
    }

    [Fact]
    public async Task Setting_a_new_default_unsets_the_old_one()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var oldDefault = await DefaultIdAsync();
        var name = $"Bawaan baru {TestData.Unique()}";

        using var response = await admin.PostFormAsync("/admin/aturan/baru", Form(name, isDefault: true));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using var db = database.CreateContext();
        var defaults = await db.RulePresets.Where(r => r.OwnerUserId == null && r.IsDefault).ToListAsync();
        Assert.Equal(name, Assert.Single(defaults).Name);
        Assert.False((await db.RulePresets.SingleAsync(r => r.Id == oldDefault)).IsDefault);

        // And back again through the edit page.
        using var restored = await admin.PostFormAsync($"/admin/aturan/{oldDefault}", await FormForExistingAsync(oldDefault, isDefault: true));
        await using var check = database.CreateContext();
        Assert.Equal(oldDefault, (await check.RulePresets.SingleAsync(r => r.OwnerUserId == null && r.IsDefault)).Id);
    }

    [Fact]
    public async Task The_default_cannot_be_switched_off_directly()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var defaultId = await DefaultIdAsync();

        using var response = await admin.PostFormAsync($"/admin/aturan/{defaultId}", await FormForExistingAsync(defaultId, isDefault: false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Harus selalu ada satu aturan bawaan.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(defaultId, await DefaultIdAsync());
    }

    [Fact]
    public async Task A_preset_used_by_an_exam_cannot_be_deleted()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        long presetId;
        await using (var db = database.CreateContext())
        {
            var preset = new RulePreset { Name = $"Dipakai {TestData.Unique()}" };
            await db.SaveAsync<Exam>(TestData.Exam().Draft().WithRules(preset));
            presetId = preset.Id;
        }

        var page = await admin.GetStringAsync(new Uri($"/admin/aturan/{presetId}", UriKind.Relative));
        using var response = await admin.PostFormAsync($"/admin/aturan/{presetId}", new Dictionary<string, string>(), postUrl: $"/admin/aturan/{presetId}?handler=Delete");

        Assert.Contains("data-testid=\"delete-blocked\"", page, StringComparison.Ordinal);
        Assert.Contains("Aturan ini dipakai oleh 1 ujian, jadi tidak bisa dihapus.", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        await using var check = database.CreateContext();
        Assert.True(await check.RulePresets.AnyAsync(r => r.Id == presetId));
    }

    [Fact]
    public async Task The_default_cannot_be_deleted_but_an_unused_preset_can()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var defaultId = await DefaultIdAsync();
        var name = $"Sekali pakai {TestData.Unique()}";
        using var created = await admin.PostFormAsync("/admin/aturan/baru", Form(name));
        var unusedId = long.Parse(created.Headers.Location!.OriginalString.Split('/')[^1], CultureInfo.InvariantCulture);

        using var defaultDelete = await admin.PostFormAsync($"/admin/aturan/{defaultId}", new Dictionary<string, string>(), postUrl: $"/admin/aturan/{defaultId}?handler=Delete");
        using var unusedDelete = await admin.PostFormAsync($"/admin/aturan/{unusedId}", new Dictionary<string, string>(), postUrl: $"/admin/aturan/{unusedId}?handler=Delete");

        Assert.Contains("Aturan bawaan tidak bisa dihapus.", WebUtility.HtmlDecode(await defaultDelete.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal("/admin/aturan", unusedDelete.Headers.Location?.OriginalString);
        await using var db = database.CreateContext();
        Assert.True(await db.RulePresets.AnyAsync(r => r.Id == defaultId));
        Assert.False(await db.RulePresets.AnyAsync(r => r.Id == unusedId));
    }

    [Theory]
    [InlineData("abc", "", "Batas waktu (menit) harus berupa angka bulat.")]
    [InlineData("0", "", "Batas waktu (menit) harus 1 sampai 600.")]
    [InlineData("", "101", "Nilai lulus (%) harus 0 sampai 100.")]
    [InlineData("", "6,5", "Nilai lulus (%) harus berupa angka bulat.")]
    public async Task Bad_limits_are_reported_and_not_saved(string time, string pass, string expected)
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        var name = $"Salah {TestData.Unique()}";

        using var response = await admin.PostFormAsync("/admin/aturan/baru", Form(name, time: time, pass: pass));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expected, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        await using var db = database.CreateContext();
        Assert.False(await db.RulePresets.AnyAsync(r => r.Name == name));
    }

    [Fact]
    public async Task List_shows_the_default_first_and_learners_get_403()
    {
        await using var app = await StartAsync();
        using var admin = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Admin);
        using var learner = await app.CreateSignedInClientAsync(TestData.User().Build(), RoleNames.Learner);

        var list = await admin.GetStringAsync(new Uri("/admin/aturan", UriKind.Relative));
        using var forbidden = await learner.GetAsync(new Uri("/admin/aturan", UriKind.Relative));
        using var missing = await admin.GetAsync(new Uri("/admin/aturan/999999999", UriKind.Relative));

        var firstRow = list.IndexOf("data-testid=\"preset-row\"", StringComparison.Ordinal);
        var secondRow = list.IndexOf("data-testid=\"preset-row\"", firstRow + 1, StringComparison.Ordinal);
        var badge = list.IndexOf("data-testid=\"default-badge\"", StringComparison.Ordinal);
        Assert.True(badge > firstRow && (secondRow < 0 || badge < secondRow), "The default preset is listed first");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    // ---------- Helpers ----------

    private async Task<AppFactory> StartAsync()
    {
        var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        return app;
    }

    private async Task<long> DefaultIdAsync()
    {
        await using var db = database.CreateContext();
        return (await db.RulePresets.SingleAsync(r => r.OwnerUserId == null && r.IsDefault)).Id;
    }

    private async Task<Dictionary<string, string>> FormForExistingAsync(long id, bool isDefault)
    {
        await using var db = database.CreateContext();
        var preset = await db.RulePresets.SingleAsync(r => r.Id == id);
        string Text(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";
        return Form(preset.Name, Text(preset.TimeLimitMinutes), Text(preset.QuestionCount), Text(preset.MaxAttempts), Text(preset.PassMarkPercent),
            preset.ShowAnswers, preset.HintsEnabled, preset.ShuffleQuestions, preset.ShuffleOptions, isDefault);
    }

    private static Dictionary<string, string> Form(
        string name,
        string time = "",
        string count = "",
        string attempts = "",
        string pass = "",
        ShowAnswers show = ShowAnswers.AfterSubmit,
        bool hints = true,
        bool shuffleQuestions = true,
        bool shuffleOptions = true,
        bool isDefault = false)
    {
        var form = new Dictionary<string, string>
        {
            ["Input.Name"] = name,
            ["Input.TimeLimitMinutes"] = time,
            ["Input.QuestionCount"] = count,
            ["Input.MaxAttempts"] = attempts,
            ["Input.PassMarkPercent"] = pass,
            ["Input.ShowAnswers"] = show.ToString(),
        };
        if (hints) { form["Input.HintsEnabled"] = "true"; }
        if (shuffleQuestions) { form["Input.ShuffleQuestions"] = "true"; }
        if (shuffleOptions) { form["Input.ShuffleOptions"] = "true"; }
        if (isDefault) { form["Input.IsDefault"] = "true"; }
        return form;
    }
}
