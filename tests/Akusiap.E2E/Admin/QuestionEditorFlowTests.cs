using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Admin;

/// <summary>ADM-T2 on a phone: the Alpine option editor (add, mark correct, reorder, true/false) and saving.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class QuestionEditorFlowTests(MobileSite site)
{
    [Fact]
    public async Task Admin_writes_a_question_with_the_option_editor_on_a_phone()
    {
        var admin = await site.CreateUserAsync(TestData.User().Build(), "admin-sandi-2026", RoleNames.Admin);
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await SignInAsync(page, admin.Email!);
        await CreateTopicAsync(page, $"Subjek {TestData.Unique()}", "Penalaran Umum");

        await page.GotoAsync("/admin/soal/baru");
        await page.GetByLabel("Topik").SelectOptionAsync(new SelectOptionValue { Label = "Penalaran Umum" });
        await page.GetByLabel("Teks soal").FillAsync("Berapakah 6 × 7?");
        var rows = page.GetByTestId("option-row");
        Assert.Equal(4, await rows.CountAsync());

        // Fill A–D, add E, and pick B then D: single choice keeps only one answer marked.
        string[] texts = ["40", "41", "42", "43"];
        for (var i = 0; i < texts.Length; i++)
        {
            await rows.Nth(i).Locator("input[type=text]").FillAsync(texts[i]);
        }

        await page.GetByRole(AriaRole.Button, new() { Name = "Tambah pilihan" }).ClickAsync();
        await rows.Nth(4).Locator("input[type=text]").FillAsync("44");
        await rows.Nth(1).GetByRole(AriaRole.Checkbox).CheckAsync();
        await rows.Nth(2).GetByRole(AriaRole.Checkbox).CheckAsync();
        Assert.False(await rows.Nth(1).GetByRole(AriaRole.Checkbox).IsCheckedAsync());

        // Move "42" (C) to the top, then remove the last option.
        await rows.Nth(2).GetByRole(AriaRole.Button, new() { Name = "Pindah ke atas" }).ClickAsync();
        await rows.Nth(1).GetByRole(AriaRole.Button, new() { Name = "Pindah ke atas" }).ClickAsync();
        await rows.Nth(4).GetByRole(AriaRole.Button, new() { Name = "Hapus pilihan" }).ClickAsync();
        Assert.Equal("42", await rows.Nth(0).Locator("input[type=text]").InputValueAsync());
        Assert.Equal(4, await rows.CountAsync());

        // True/false swaps in the two fixed options; switching back restores the typed ones.
        await page.GetByLabel("Jenis soal").SelectOptionAsync("TrueFalse");
        Assert.Equal(2, await rows.CountAsync());
        Assert.Equal("Benar", await rows.Nth(0).Locator("input[type=text]").InputValueAsync());
        Assert.NotNull(await rows.Nth(0).Locator("input[type=text]").GetAttributeAsync("readonly"));
        await page.GetByLabel("Jenis soal").SelectOptionAsync("SingleChoice");
        Assert.Equal(4, await rows.CountAsync());
        Assert.Equal("42", await rows.Nth(0).Locator("input[type=text]").InputValueAsync());

        await page.GetByLabel("Pembahasan").FillAsync("6 × 7 = 42.");
        await page.GetByLabel("Fakta kunci (opsional)").FillAsync("6 × 7 = 42");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();

        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Perubahan disimpan.");
        Assert.Equal("42", await rows.Nth(0).Locator("input[type=text]").InputValueAsync());
        Assert.True(await rows.Nth(0).GetByRole(AriaRole.Checkbox).IsCheckedAsync());
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }

    [Fact]
    public async Task Invalid_question_shows_the_indonesian_message_and_keeps_the_options()
    {
        var admin = await site.CreateUserAsync(TestData.User().Build(), "admin-sandi-2026", RoleNames.Admin);
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        await SignInAsync(page, admin.Email!);
        await CreateTopicAsync(page, $"Subjek {TestData.Unique()}", "TIU");

        await page.GotoAsync("/admin/soal/baru");
        await page.GetByLabel("Topik").SelectOptionAsync(new SelectOptionValue { Label = "TIU" });
        await page.GetByLabel("Teks soal").FillAsync("Soal tanpa jawaban benar");
        var rows = page.GetByTestId("option-row");
        await rows.Nth(0).Locator("input[type=text]").FillAsync("Satu");
        await rows.Nth(1).Locator("input[type=text]").FillAsync("Dua");
        await rows.Nth(3).GetByRole(AriaRole.Button, new() { Name = "Hapus pilihan" }).ClickAsync();
        await rows.Nth(2).GetByRole(AriaRole.Button, new() { Name = "Hapus pilihan" }).ClickAsync();
        await page.GetByLabel("Pembahasan").FillAsync("-");
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();

        await Assertions.Expect(page.GetByTestId("option-errors")).ToContainTextAsync("Soal pilihan ganda harus punya tepat 1 jawaban benar.");
        Assert.Equal(2, await rows.CountAsync());
        Assert.Equal("Dua", await rows.Nth(1).Locator("input[type=text]").InputValueAsync());
    }

    private static async Task SignInAsync(IPage page, string email)
    {
        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(email);
        await page.GetByLabel("Kata sandi").FillAsync("admin-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.WaitForURLAsync("**/ujian");
    }

    private static async Task CreateTopicAsync(IPage page, string subject, string topic)
    {
        await page.GotoAsync("/admin/subjek/baru");
        await page.GetByLabel("Nama subjek").FillAsync(subject);
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();
        await page.GetByLabel("Nama topik").FillAsync(topic);
        await page.GetByRole(AriaRole.Button, new() { Name = "Tambah topik" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("topic-list")).ToContainTextAsync(topic);
    }
}
