using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Memorization;

/// <summary>MEM-03 on a phone: flip a card, rate it, and the next card swaps in without a page load.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class FlashcardFlowTests(MobileSite site)
{
    [Fact]
    public async Task Flipping_and_rating_swaps_in_the_next_card()
    {
        var learner = await site.CreateUserAsync(TestData.User().Build(), "kartu-sandi-2026", RoleNames.Learner);
        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Kartu {TestData.Unique()}").Free().WithQuestions(2));
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(learner.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("kartu-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/akun/masuk", StringComparison.Ordinal));

        await page.GotoAsync("/learn/kartu");
        await page.Locator($"[data-testid=deck][data-exam='{exam.Id}']").GetByTestId("start-deck").ClickAsync();
        await page.WaitForURLAsync("**/learn/kartu/*");
        var setUrl = page.Url;

        var card = page.GetByTestId("flashcard");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-position", "1");
        await Assertions.Expect(page.GetByTestId("answer")).ToBeHiddenAsync();
        await page.GetByTestId("flip").ClickAsync();
        await Assertions.Expect(page.GetByTestId("answer")).ToBeVisibleAsync();
        await page.GetByTestId("rate-Good").ClickAsync();

        await Assertions.Expect(card).ToHaveAttributeAsync("data-position", "2");
        await Assertions.Expect(page.GetByTestId("answer")).ToBeHiddenAsync();
        Assert.Equal(setUrl, page.Url);
        await page.GetByTestId("flip").ClickAsync();
        await page.GetByTestId("rate-Easy").ClickAsync();

        await Assertions.Expect(page.GetByTestId("flashcard-summary")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("summary-Good")).ToHaveTextAsync("1");
        await Assertions.Expect(page.GetByTestId("summary-Easy")).ToHaveTextAsync("1");
    }
}
