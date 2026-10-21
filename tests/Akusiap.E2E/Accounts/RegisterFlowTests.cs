using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Accounts;

/// <summary>AC-01 flow on a phone: a guest registers and lands on the catalog signed in.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class RegisterFlowTests(MobileSite site)
{
    [Fact]
    public async Task Guest_registers_on_a_phone_and_lands_on_the_catalog_signed_in()
    {
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/");

        await page.GetByRole(AriaRole.Link, new() { Name = "Daftar", Exact = true }).ClickAsync();
        await page.GetByLabel("Email").FillAsync($"sari-{TestData.Unique()}@example.com");
        await page.GetByLabel("Nama tampilan").FillAsync("Sari");
        await page.GetByLabel("Kata sandi").FillAsync("teh-manis-2026");
        await page.GetByRole(AriaRole.Button, new() { Name = "Tampilkan" }).ClickAsync();
        Assert.Equal("text", await page.GetByLabel("Kata sandi").GetAttributeAsync("type"));
        await page.Locator("form button[type=submit]").ClickAsync();

        await page.WaitForURLAsync("**/ujian");
        Assert.Equal("Sari", (await page.GetByTestId("signed-in-name").InnerTextAsync()).Trim());
        Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Keluar" }).IsVisibleAsync());
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }

    [Fact]
    public async Task Sign_in_page_fits_a_phone_and_shows_errors_in_indonesian()
    {
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/akun/masuk");

        await page.GetByLabel("Email").FillAsync($"tidak-ada-{TestData.Unique()}@example.com");
        await page.GetByLabel("Kata sandi").FillAsync("salah-sandi");
        await page.Locator("form button[type=submit]").ClickAsync();

        Assert.Contains("Email atau kata sandi salah.", await page.GetByTestId("sign-in-error").InnerTextAsync(), StringComparison.Ordinal);
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }
}
