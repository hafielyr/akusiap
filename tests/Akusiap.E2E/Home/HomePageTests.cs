using Microsoft.Playwright;

namespace Akusiap.E2E.Home;

/// <summary>
/// F-05 sample E2E, BRD-09: the landing page loads on a phone with its main action in the first screen, the FAQ opens,
/// the tagline lights up as it scrolls in, and the phone menu opens and closes.
/// </summary>
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
            "Fokus ke ujian Anda CPNS, BUMN, Kedinasan, UKMPPD",
            string.Join(' ', (await page.Locator("h1").InnerTextAsync()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
        var tryFree = await page.GetByTestId("home-try").BoundingBoxAsync();
        Assert.NotNull(tryFree);
        Assert.True(tryFree.Y + tryFree.Height <= MobileSite.Height, $"The main button ends at {tryFree.Y + tryFree.Height}px, below the first screen");
        Assert.True(await page.GetByTestId("whatsapp-support").IsVisibleAsync(), "The WhatsApp support button is visible");
        Assert.True(await page.GetByTestId("home-art").IsVisibleAsync(), "The hero art is visible");
        Assert.Equal(4, await page.GetByTestId("home-category").CountAsync());
        Assert.Equal(3, await page.GetByTestId("home-benefits").Locator("> li").CountAsync());
        Assert.Equal(8, await page.GetByTestId("home-faq").Locator("details").CountAsync());
        Assert.Equal("Coba 30 soal gratis →", string.Join(' ', (await page.GetByTestId("home-cta").InnerTextAsync()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));

        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }

    [Fact]
    public async Task Faq_opens_and_the_tagline_lights_up_when_scrolled_into_view()
    {
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/");

        var firstQuestion = page.GetByTestId("home-faq").Locator("details").First;
        await firstQuestion.Locator("summary").ClickAsync();
        await Assertions.Expect(firstQuestion).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(firstQuestion.Locator("p")).ToBeVisibleAsync();

        var words = page.GetByTestId("home-tagline").Locator(".tagline-word");
        Assert.True(await words.CountAsync() >= 2, "The tagline has words to light up");
        await page.GetByTestId("home-tagline").ScrollIntoViewIfNeededAsync();
        await page.Mouse.WheelAsync(0, 400);
        await Assertions.Expect(words.Last).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bis-on\b"));
    }

    [Fact]
    public async Task Phone_menu_opens_with_the_main_links_and_closes_with_escape()
    {
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/");
        var panel = page.GetByTestId("menu-panel");

        await Assertions.Expect(panel).ToBeHiddenAsync();
        await page.GetByTestId("menu-toggle").ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        foreach (var name in new[] { "Katalog", "Cari soal", "Harga" })
        {
            await Assertions.Expect(panel.GetByRole(AriaRole.Link, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        }

        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToBeHiddenAsync();

        await page.GetByTestId("menu-toggle").ClickAsync();
        await panel.GetByRole(AriaRole.Link, new() { Name = "Harga", Exact = true }).ClickAsync();
        await page.WaitForURLAsync("**/harga");
    }
}
