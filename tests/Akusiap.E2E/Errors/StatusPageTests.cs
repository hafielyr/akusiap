namespace Akusiap.E2E.Errors;

/// <summary>AC-06: the not-found page is friendly and fits a phone.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class StatusPageTests(MobileSite site)
{
    [Fact]
    public async Task Not_found_page_is_indonesian_and_fits_a_360px_phone()
    {
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();

        var response = await page.GotoAsync("/tidak-ada-halaman-ini");

        Assert.Equal(404, response?.Status);
        Assert.Equal("Halaman tidak ditemukan.", (await page.Locator("h1").InnerTextAsync()).Trim());
        Assert.True(await page.GetByRole(Microsoft.Playwright.AriaRole.Link, new() { Name = "Kembali ke beranda" }).IsVisibleAsync());
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }
}
