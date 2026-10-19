using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Catalog;

/// <summary>CAT-01 on a phone: the debounced htmx search narrows the catalog without a reload and updates the URL.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class CatalogFlowTests(MobileSite site)
{
    [Fact]
    public async Task Typing_a_search_narrows_the_catalog_without_a_reload()
    {
        var token = TestData.Unique();
        var admin = await site.CreateUserAsync(TestData.User().Build(), "admin-sandi-2026", RoleNames.Admin);
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();

        // Publish two exams through the admin pages: a free one and a paid one with a full trial set.
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(admin.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("admin-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync("**/ujian");
        await CreateTopicAndQuestionAsync(page, token);
        await PublishExamAsync(page, $"Katalog gratis {token}", free: true);
        await PublishExamAsync(page, $"Katalog lain {token}", free: true);
        await page.GetByRole(AriaRole.Button, new() { Name = "Keluar" }).ClickAsync();

        await page.GotoAsync("/ujian");
        await page.EvaluateAsync("() => { window.__noReload = true; }");
        await page.GetByPlaceholder("Cari judul atau deskripsi ujian").FillAsync($"gratis {token}");

        await Assertions.Expect(page.GetByTestId("catalog-card")).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("catalog-card")).ToContainTextAsync($"Katalog gratis {token}");
        await Assertions.Expect(page.GetByTestId("badge")).ToHaveTextAsync("Gratis");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__noReload === true"), "The page was fully reloaded");
        Assert.Contains("q=gratis", page.Url, StringComparison.Ordinal);
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }

    private static async Task CreateTopicAndQuestionAsync(IPage page, string token)
    {
        await page.GotoAsync("/admin/subjek/baru");
        await page.GetByLabel("Nama subjek").FillAsync($"Subjek {token}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await page.GetByLabel("Nama topik").FillAsync($"Topik {token}");
        await page.GetByRole(AriaRole.Button, new() { Name = "Tambah topik" }).ClickAsync();
        await page.GotoAsync("/admin/soal/baru");
        await page.GetByLabel("Topik").SelectOptionAsync(new SelectOptionValue { Label = $"Topik {token}" });
        await page.GetByLabel("Jenis soal").SelectOptionAsync("TrueFalse");
        await page.GetByLabel("Teks soal").FillAsync($"Soal katalog {token}");
        await page.GetByLabel("Pembahasan").FillAsync("Pembahasan.");
        await page.GetByLabel("Status").SelectOptionAsync("Active");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Perubahan disimpan.");
    }

    private static async Task PublishExamAsync(IPage page, string title, bool free)
    {
        await page.GotoAsync("/admin/ujian/baru");
        await page.GetByLabel("Judul ujian").FillAsync(title);
        if (free)
        {
            await page.GetByLabel("Gratis untuk pengguna terdaftar").CheckAsync();
        }

        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Ujian dibuat sebagai draf.");
        await page.GetByLabel("Cari teks soal").FillAsync(title.Split(' ')[^1]);
        var row = page.Locator("#question-search-results li").First;
        await row.GetByRole(AriaRole.Button, new() { Name = "Tambah" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("count-total")).ToHaveTextAsync("1 soal");
        await page.GetByRole(AriaRole.Button, new() { Name = "Terbitkan ujian" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Ujian diterbitkan dan tampil di katalog.");
    }
}
