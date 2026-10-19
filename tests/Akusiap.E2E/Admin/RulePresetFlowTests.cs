using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Admin;

/// <summary>ADM-T5 on a phone: create a timed preset, make it the default, and put the old default back.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class RulePresetFlowTests(MobileSite site)
{
    [Fact]
    public async Task Admin_makes_a_new_preset_the_default_on_a_phone()
    {
        var admin = await site.CreateUserAsync(TestData.User().Build(), "admin-sandi-2026", RoleNames.Admin);
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        var name = $"Simulasi SKD {TestData.Unique()}";

        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(admin.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("admin-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.GotoAsync("/admin/aturan");
        var oldDefault = page.GetByTestId("preset-row").Filter(new() { Has = page.GetByTestId("default-badge") });
        var oldDefaultName = (await oldDefault.Locator("span.font-semibold").InnerTextAsync()).Trim();

        await page.GetByRole(AriaRole.Link, new() { Name = "Tambah aturan" }).ClickAsync();
        await page.GetByLabel("Nama aturan").FillAsync(name);
        await page.GetByLabel("Batas waktu (menit)").FillAsync("100");
        await page.GetByLabel("Setelah sesi selesai").CheckAsync();
        await page.GetByLabel("Jadikan aturan bawaan untuk ujian baru").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Perubahan disimpan.");

        await page.GotoAsync("/admin/aturan");
        var rows = page.GetByTestId("preset-row");
        await Assertions.Expect(rows.First).ToContainTextAsync(name);
        await Assertions.Expect(rows.First).ToContainTextAsync("100 menit");
        await Assertions.Expect(page.GetByTestId("default-badge")).ToHaveCountAsync(1);
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");

        // Put the old default back so other tests in this run see the seeded default.
        await rows.Filter(new() { HasText = oldDefaultName }).ClickAsync();
        await page.GetByLabel("Jadikan aturan bawaan untuk ujian baru").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("default-badge")).ToHaveCountAsync(1);
    }
}
