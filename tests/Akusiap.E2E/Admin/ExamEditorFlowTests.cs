using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Admin;

/// <summary>ADM-T3 on a phone: create a paid exam (it starts as Draft), then make it free.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class ExamEditorFlowTests(MobileSite site)
{
    [Fact]
    public async Task Admin_creates_a_paid_draft_exam_then_makes_it_free_on_a_phone()
    {
        var admin = await site.CreateUserAsync(TestData.User().Build(), "admin-sandi-2026", RoleNames.Admin);
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        var title = $"Tryout SKD {TestData.Unique()}";

        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(admin.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("admin-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.GotoAsync("/admin");
        await page.GetByTestId("admin-nav").GetByRole(AriaRole.Link, new() { Name = "Ujian" }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Tambah ujian" }).ClickAsync();

        await page.GetByLabel("Judul ujian").FillAsync(title);
        await page.GetByLabel("Harga (Rp)").FillAsync("Rp49.000");
        await page.GetByLabel("Masa akses (hari)").FillAsync("30");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();

        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Ujian dibuat sebagai draf.");
        await Assertions.Expect(page.GetByTestId("exam-status")).ToHaveTextAsync("Draf");
        Assert.Equal("Rp49.000", await page.GetByLabel("Harga (Rp)").InputValueAsync());

        await page.GetByLabel("Gratis untuk pengguna terdaftar").CheckAsync();
        Assert.False(await page.GetByLabel("Harga (Rp)").IsVisibleAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();

        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Perubahan disimpan.");
        Assert.True(await page.GetByLabel("Gratis untuk pengguna terdaftar").IsCheckedAsync());
        await page.GotoAsync("/admin/ujian");
        await Assertions.Expect(page.GetByTestId("exam-list")).ToContainTextAsync(title);
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }
}
