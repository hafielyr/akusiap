using Akusiap.Domain.Access;
using Akusiap.Domain.Payments;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Payments;

/// <summary>PAY-05 on a phone: the countdown on the payment page reaches 0 and shows that the time ran out.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class PaymentCountdownTests(MobileSite site)
{
    [Fact]
    public async Task The_countdown_reaches_zero_and_shows_expired()
    {
        var user = await site.CreateUserAsync(TestData.User().Build(), "kopi-susu-2026", Akusiap.Domain.Users.RoleNames.Learner);
        var exam = await site.SaveExamAsync(TestData.Exam().Paid(49_000).WithQuestions(1, trial: 1));
        var order = OrderNumbers.Create(site.Now, new Random());
        await site.WithDatabaseAsync(async db =>
        {
            var purchase = new ExamPurchase { UserId = user.Id, ExamId = exam.Id, PricePaidIdr = 49_000 };
            db.ExamPurchases.Add(purchase);
            await db.SaveChangesAsync();
            db.Payments.Add(new Payment
            {
                OrderNumber = order, InvoiceNumber = order, UserId = user.Id, ExamPurchaseId = purchase.Id, AmountIdr = 49_000,
                Method = PaymentMethod.QRIS, QrString = "FAKE-QRIS|" + order, CreatedAt = site.Now, ExpiresAt = site.Now.AddSeconds(3),
            });
            await db.SaveChangesAsync();
        });

        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(user.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("kopi-susu-2026");
        await page.Locator("form button[type=submit]").Last.ClickAsync();
        await page.WaitForURLAsync("**/ujian");
        await page.GotoAsync($"/bayar/{order}");

        await Assertions.Expect(page.GetByTestId("pay-qr")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("countdown-expired")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }
}
