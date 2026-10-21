using Microsoft.Playwright;

namespace Akusiap.E2E.Home;

/// <summary>F-05 sample E2E: the home page loads on a phone.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class HomePageTests(MobileSite site)
{
    [Fact]
    public async Task Home_page_loads_in_indonesian_and_fits_a_360px_phone()
    {
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();

        var response = await page.GotoAsync("/");

        Assert.True(response?.Ok, $"GET / returned {response?.Status}");
        Assert.Equal("id", await page.Locator("html").GetAttributeAsync("lang"));
        Assert.Equal(
            "Latihan soal CPNS, BUMN, Kedinasan & UKMPPD dalam satu aplikasi",
            (await page.Locator("h1").InnerTextAsync()).Trim());
        Assert.True(await page.GetByTestId("whatsapp-support").IsVisibleAsync(), "The WhatsApp support button is visible");
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }
}
