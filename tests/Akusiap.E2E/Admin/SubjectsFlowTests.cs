using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Admin;

/// <summary>ADM-T1 on a phone: an admin signs in, opens Admin and adds a subject and a topic.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class SubjectsFlowTests(MobileSite site)
{
    [Fact]
    public async Task Admin_adds_a_subject_and_a_topic_on_a_phone()
    {
        var admin = await site.CreateUserAsync(TestData.User().Named("Admin Uji").Build(), "admin-sandi-2026", RoleNames.Admin);
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();
        var subjectName = $"Seleksi CPNS {TestData.Unique()}";

        await page.GotoAsync("/akun/masuk");
        await page.GetByLabel("Email").FillAsync(admin.Email!);
        await page.GetByLabel("Kata sandi").FillAsync("admin-sandi-2026");
        await page.Locator("form button[type=submit]").ClickAsync();
        await page.GetByTestId("admin-link").ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Subjek & topik" }).First.ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Tambah subjek" }).ClickAsync();
        await page.GetByLabel("Nama subjek").FillAsync(subjectName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Simpan" }).ClickAsync();

        await Assertions.Expect(page.GetByTestId("flash")).ToHaveTextAsync("Perubahan disimpan.");
        await page.GetByLabel("Nama topik").FillAsync("Tes Wawasan Kebangsaan");
        await page.GetByRole(AriaRole.Button, new() { Name = "Tambah topik" }).ClickAsync();

        await Assertions.Expect(page.GetByTestId("topic-list")).ToContainTextAsync("Tes Wawasan Kebangsaan");
        await Assertions.Expect(page.GetByTestId("topic-list")).ToContainTextAsync("0 soal");
        var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 0, $"The page scrolls sideways by {overflow}px at {MobileSite.Width}px");
    }
}
