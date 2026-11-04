using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Payments;

/// <summary>
/// PAY-12 on a phone: the summary and the first payment methods fit the first screen, "Bayar" stays on screen while the
/// learner scrolls the methods, and picking a method and paying opens the payment instructions.
/// </summary>
[Collection(UsesMobileSite.Name)]
public sealed class CheckoutFlowTests(MobileSite site)
{
    [Fact]
    public async Task Pay_stays_on_screen_and_paying_opens_the_instructions()
    {
        var user = await site.CreateUserAsync(TestData.User().Build(), "kopi-susu-2026", Akusiap.Domain.Users.RoleNames.Learner);
        var exam = await site.SaveExamAsync(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1));
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(user.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("kopi-susu-2026");
        await page.Locator("form button[type=submit]").Last.ClickAsync();
        await page.WaitForURLAsync("**/ujian");

        await page.GotoAsync($"/checkout?exam={exam.Id}");
        var pay = page.GetByTestId("pay");
        var viewport = page.ViewportSize!.Height;
        Assert.True(await InViewAsync(page.GetByTestId("checkout-summary"), viewport), "The summary is on the first screen");
        Assert.True(await InViewAsync(page.GetByTestId("channel").First, viewport), "A payment method is on the first screen");
        Assert.True(await InViewAsync(pay, viewport), "Bayar is on the first screen");

        await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 2)");
        Assert.True(await InViewAsync(pay, viewport), "Bayar stays on screen while scrolling");

        await page.Locator("[data-testid=channel][data-code=VA-BCA]").CheckAsync();
        await page.GetByTestId("accept-terms").CheckAsync();
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        await pay.ClickAsync();

        await page.WaitForURLAsync("**/bayar/**");
        await Assertions.Expect(page.GetByTestId("pay-va")).ToBeVisibleAsync();
        Assert.True(overflow <= 0, $"Checkout scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }

    private static async Task<bool> InViewAsync(ILocator locator, int viewportHeight)
    {
        var box = await locator.BoundingBoxAsync();
        return box is not null && box.Y >= 0 && box.Y + box.Height <= viewportHeight;
    }
}
