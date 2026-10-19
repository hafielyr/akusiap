using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Sharing;

/// <summary>SHR-05 on phones: the owner shares → a guest takes the exam and reports it → an admin takes it down → the link is blocked.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class SharingFlowTests(MobileSite site)
{
    [Fact]
    public async Task Share_take_report_and_take_down()
    {
        var owner = await site.CreateUserAsync(TestData.User().Build(), "pemilik-sandi-2026", RoleNames.Learner);
        var admin = await site.CreateUserAsync(TestData.User().Build(), "admin-sandi-2026", RoleNames.Admin);
        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Bagikan {TestData.Unique()}").OwnedBy(owner).WithQuestions(3));

        // The owner shares the exam and copies the link.
        await using var ownerPhone = await site.NewPhoneAsync();
        var ownerPage = await ownerPhone.NewPageAsync();
        await SignInAsync(ownerPage, owner.Email!, "pemilik-sandi-2026");
        await ownerPage.GotoAsync($"/learn/ujian-saya/{exam.Id}");
        await ownerPage.GetByTestId("share-start").ClickAsync();
        var link = await ownerPage.GetByTestId("share-link").InputValueAsync();
        var path = new Uri(link).AbsolutePath;

        // A guest opens the link, needs a name, starts, then reports the exam.
        await using var guestPhone = await site.NewPhoneAsync();
        var guestPage = await guestPhone.NewPageAsync();
        await guestPage.GotoAsync(path);
        await Assertions.Expect(guestPage.GetByTestId("shared-notice")).ToBeVisibleAsync();
        await guestPage.GetByLabel("Nama Anda").FillAsync("Sari");
        await guestPage.GetByTestId("shared-start").ClickAsync();
        await guestPage.WaitForURLAsync("**/sesi/*");
        await Assertions.Expect(guestPage.GetByTestId("question")).ToBeVisibleAsync();
        await guestPage.GotoAsync($"{path}/lapor");
        await guestPage.GetByLabel("Melanggar hak cipta").CheckAsync();
        await guestPage.GetByRole(AriaRole.Button, new() { Name = "Kirim laporan" }).ClickAsync();
        await Assertions.Expect(guestPage.GetByTestId("report-sent")).ToBeVisibleAsync();

        // An admin takes it down from the report queue.
        await using var adminPhone = await site.NewPhoneAsync();
        var adminPage = await adminPhone.NewPageAsync();
        await SignInAsync(adminPage, admin.Email!, "admin-sandi-2026");
        await adminPage.GotoAsync("/admin/laporan?tab=ujian");
        await adminPage.Locator($"[data-testid=exam-report-group][data-exam='{exam.Id}']").GetByTestId("take-down").ClickAsync();
        await Assertions.Expect(adminPage.GetByTestId("flash")).ToHaveTextAsync("Ujian diturunkan.");

        // The link is blocked for the guest.
        await guestPage.GotoAsync(path);
        await Assertions.Expect(guestPage.GetByTestId("shared-unavailable")).ToContainTextAsync("Ujian tidak tersedia");
        await Assertions.Expect(guestPage.GetByTestId("shared-start")).ToHaveCountAsync(0);
    }

    private static async Task SignInAsync(IPage page, string email, string password)
    {
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Kata sandi").FillAsync(password);
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync(url => !url.Contains("/akun/masuk", StringComparison.Ordinal));
    }
}
