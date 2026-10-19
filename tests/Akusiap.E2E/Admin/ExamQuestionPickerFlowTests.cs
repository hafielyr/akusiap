using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Admin;

/// <summary>ADM-T4 on a phone: htmx add, trial toggle and remove update the counters without a full page reload.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class ExamQuestionPickerFlowTests(MobileSite site)
{
    [Fact]
    public async Task Counters_update_without_a_full_page_reload()
    {
        var admin = await site.CreateUserAsync(TestData.User().Build(), "admin-sandi-2026", RoleNames.Admin);
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        var token = TestData.Unique();

        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(admin.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("admin-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync("**/ujian");

        // A topic and an active question to add, made through the admin pages.
        await page.GotoAsync("/admin/subjek/baru");
        await page.GetByLabel("Nama subjek").FillAsync($"Subjek {token}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await page.GetByLabel("Nama topik").FillAsync("Topik pilih");
        await page.GetByRole(AriaRole.Button, new() { Name = "Tambah topik" }).ClickAsync();
        await page.GotoAsync("/admin/soal/baru");
        await page.GetByLabel("Topik").SelectOptionAsync(new SelectOptionValue { Label = "Topik pilih" });
        await page.GetByLabel("Jenis soal").SelectOptionAsync("TrueFalse");
        await page.GetByLabel("Teks soal").FillAsync($"Soal pilih {token}");
        await page.GetByLabel("Pembahasan").FillAsync("Pembahasan.");
        await page.GetByLabel("Status").SelectOptionAsync("Active");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Perubahan disimpan.");

        // A paid draft exam.
        await page.GotoAsync("/admin/ujian/baru");
        await page.GetByLabel("Judul ujian").FillAsync($"Ujian pilih {token}");
        await page.GetByLabel("Harga (Rp)").FillAsync("49000");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Ujian dibuat sebagai draf.");
        var examUrl = page.Url;

        // Mark the page: a full reload would clear this.
        await page.EvaluateAsync("() => { window.__noReload = true; }");
        await Assertions.Expect(page.GetByTestId("count-total")).ToHaveTextAsync("0 soal");

        await page.GetByLabel("Cari teks soal").FillAsync(token);
        var row = page.Locator("#question-search-results li").Filter(new() { HasText = $"Soal pilih {token}" });
        await Assertions.Expect(row).ToHaveCountAsync(1);
        await row.GetByRole(AriaRole.Button, new() { Name = "Tambah" }).ClickAsync();

        await Assertions.Expect(page.GetByTestId("count-total")).ToHaveTextAsync("1 soal");
        await Assertions.Expect(row.GetByTestId("already-added")).ToHaveTextAsync("Sudah ada");
        await page.GetByTestId("exam-question-row").GetByLabel("Soal coba").CheckAsync();
        await Assertions.Expect(page.GetByTestId("count-trial")).ToHaveTextAsync("1 soal coba");
        await page.GetByTestId("exam-question-row").GetByRole(AriaRole.Button, new() { Name = "Keluarkan dari ujian" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("count-total")).ToHaveTextAsync("0 soal");

        Assert.True(await page.EvaluateAsync<bool>("() => window.__noReload === true"), "The page was fully reloaded");
        Assert.Equal(examUrl, page.Url);
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }
}
