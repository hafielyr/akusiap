using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Performance;

/// <summary>OPS-01: the first load of the catalog and the player stays within 150 KB transferred (compressed).</summary>
[Collection(UsesMobileSite.Name)]
public sealed class PerformanceBudgetTests(MobileSite site, Xunit.Abstractions.ITestOutputHelper output)
{
    private const long BudgetBytes = 150 * 1024;

    [Fact]
    public async Task Catalog_and_player_first_loads_stay_within_150_KB()
    {
        for (var i = 0; i < 20; i++)
        {
            await site.SaveExamAsync(TestData.Exam().Titled($"Anggaran {TestData.Unique()}").Paid(49_000).WithQuestions(5, trial: 5));
        }

        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Pemain anggaran {TestData.Unique()}").Paid(49_000).WithQuestions(30, trial: 30));

        await using var catalogPhone = await site.NewPhoneAsync();
        var catalog = await catalogPhone.NewPageAsync();
        var catalogBytes = await TransferredAsync(catalog, () => catalog.GotoAsync("/ujian"));

        // Start the session in one browser, then open the player in a fresh one (cold cache) with the same guest cookie.
        await using var setupPhone = await site.NewPhoneAsync();
        var setup = await setupPhone.NewPageAsync();
        await setup.GotoAsync($"/ujian/{exam.Id}/mulai");
        await setup.GetByTestId("start").ClickAsync();
        await setup.WaitForURLAsync("**/sesi/*");
        var playerUrl = setup.Url;
        var cookies = await setup.Context.CookiesAsync();

        await using var playerPhone = await site.NewPhoneAsync();
        var player = await playerPhone.NewPageAsync();
        await player.Context.AddCookiesAsync(cookies.Select(c => new Cookie
        {
            Name = c.Name, Value = c.Value, Domain = c.Domain, Path = c.Path, HttpOnly = c.HttpOnly, Secure = c.Secure, SameSite = c.SameSite,
        }));
        var playerBytes = await TransferredAsync(player, () => player.GotoAsync(playerUrl));
        await Assertions.Expect(player.GetByTestId("question")).ToBeVisibleAsync();

        output.WriteLine($"Catalog: {catalogBytes / 1024.0:F1} KB, player: {playerBytes / 1024.0:F1} KB");
        Assert.True(catalogBytes <= BudgetBytes, $"Catalog first load transferred {catalogBytes / 1024.0:F1} KB");
        Assert.True(playerBytes <= BudgetBytes, $"Player first load transferred {playerBytes / 1024.0:F1} KB");
    }

    [Fact]
    public async Task Pages_are_compressed()
    {
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();

        var response = await page.GotoAsync("/ujian");

        Assert.True(response!.Headers["content-encoding"] is "br" or "gzip", response.Headers["content-encoding"]);
    }

    /// <summary>
    /// Bytes on the wire for the page and everything it loads, with a cold cache: the browser's Resource Timing
    /// <c>transferSize</c> (headers plus the encoded body) for the navigation and every resource.
    /// </summary>
    private static async Task<long> TransferredAsync(IPage page, Func<Task> navigate)
    {
        await navigate();
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        return await page.EvaluateAsync<long>(
            "() => [...performance.getEntriesByType('navigation'), ...performance.getEntriesByType('resource')].reduce((sum, e) => sum + e.transferSize, 0)");
    }
}
