using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Catalog;

/// <summary>CAT-02 on a phone: a guest opens a free exam from the catalog, registers to unlock it and comes back to it.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class ExamPageFlowTests(MobileSite site)
{
    [Fact]
    public async Task Guest_registers_from_a_free_exam_and_returns_to_it_unlocked()
    {
        var token = TestData.Unique();
        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Ujian gratis {token}").Free().WithQuestions(3));
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();

        await page.GotoAsync($"/ujian?q={token}");
        await page.GetByTestId("catalog-card").ClickAsync();
        await page.WaitForURLAsync($"**/ujian/{exam.Id}");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync($"Ujian gratis {token}");
        await AssertFitsAsync(page);

        await page.GetByRole(AriaRole.Link, new() { Name = "Daftar gratis untuk membuka" }).ClickAsync();
        await page.GetByLabel("Email").FillAsync($"rina-{token}@example.com");
        await page.GetByLabel("Nama tampilan").FillAsync("Rina");
        await page.GetByLabel("Kata sandi").FillAsync("kopi-susu-2026");
        await page.Locator("form button[type=submit]").ClickAsync();

        await page.WaitForURLAsync($"**/ujian/{exam.Id}");
        await Assertions.Expect(page.GetByTestId("cta-start")).ToHaveTextAsync("Mulai latihan");
        await Assertions.Expect(page.GetByTestId("cta-register")).ToHaveCountAsync(0);
        await AssertFitsAsync(page);
    }

    private static async Task AssertFitsAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }
}
