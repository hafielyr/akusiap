using Microsoft.Playwright;

namespace Akusiap.E2E.Design;

/// <summary>BRD-03: the theme toggle switches to dark, the choice survives a reload, and dark pages still fit 360 px.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class ThemeToggleTests(MobileSite site)
{
    [Fact]
    public async Task The_dark_theme_is_remembered_and_pages_still_fit_a_phone()
    {
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/");
        var html = page.Locator("html");
        var lightBackground = await BodyBackgroundAsync(page);

        await page.GetByTestId("theme-toggle").ClickAsync();
        await Assertions.Expect(html).ToHaveAttributeAsync("data-theme", "dark");
        await Assertions.Expect(page.GetByTestId("theme-toggle")).ToHaveAttributeAsync("aria-pressed", "true");
        await page.ReloadAsync();

        await Assertions.Expect(html).ToHaveAttributeAsync("data-theme", "dark");
        Assert.NotEqual(lightBackground, await BodyBackgroundAsync(page));
        Assert.True(await IsDarkAsync(page), "The body background is dark");
        Assert.True(await page.GetByTestId("home-showcase").IsVisibleAsync());
        Assert.Equal(0, await OverflowAsync(page));

        await page.GotoAsync("/ujian");
        await Assertions.Expect(html).ToHaveAttributeAsync("data-theme", "dark");
        Assert.Equal(0, await OverflowAsync(page));

        await page.GetByTestId("theme-toggle").ClickAsync();
        await page.ReloadAsync();
        Assert.Null(await html.GetAttributeAsync("data-theme")); // back to light: nothing is set on load
    }

    private static Task<string> BodyBackgroundAsync(IPage page) =>
        page.EvaluateAsync<string>("() => getComputedStyle(document.body).backgroundColor");

    // The canvas colour's brightness (0-255): the dark canvas is far below the middle.
    private static async Task<bool> IsDarkAsync(IPage page) =>
        await page.EvaluateAsync<bool>("() => { const m = getComputedStyle(document.body).backgroundColor.match(/\\d+/g).map(Number); return (m[0] + m[1] + m[2]) / 3 < 60; }");

    private static async Task<int> OverflowAsync(IPage page) =>
        Math.Max(0, await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth"));
}
