using Akusiap.Domain.Content;
using Akusiap.TestSupport.Builders;
using Microsoft.Playwright;

namespace Akusiap.E2E.Catalog;

/// <summary>CPNS-01 and CPNS-02 on a phone: search the test dates, open a test, see its countdown, syllabus and package.</summary>
[Collection(UsesMobileSite.Name)]
public sealed class UpcomingTestsFlowTests(MobileSite site)
{
    [Fact]
    public async Task A_job_seeker_finds_a_test_and_its_package_on_a_phone()
    {
        var tag = TestData.Unique();
        var exam = await site.SaveExamAsync(TestData.Exam().Titled($"Paket {tag}").Paid(50_000, accessDays: 180).WithQuestions(30, trial: 20));
        await site.WithDatabaseAsync(async db =>
        {
            db.UpcomingTests.Add(new UpcomingTest
            {
                Slug = $"cpns-{tag}",
                Name = $"CPNS {tag}",
                Organizer = "BKN dan KemenPAN-RB",
                Category = UpcomingTestCategory.Cpns,
                Summary = "Seleksi calon pegawai negeri sipil.",
                TestStartsOn = DateOnly.FromDateTime(site.Now).AddDays(70),
                ScheduleStatus = ScheduleStatus.Estimated,
                Outline = "Format SKD\n110 soal dalam 100 menit\n\nTes Wawasan Kebangsaan (TWK), 30 soal\nNasionalisme\nIntegritas",
                ExamId = exam.Id,
                IsPublished = true,
                CreatedAt = site.Now,
                UpdatedAt = site.Now,
            });
            await db.SaveChangesAsync();
        });
        await using var phone = await site.NewPhoneAsync();
        var page = await phone.NewPageAsync();

        await page.GotoAsync("/jadwal-tes");
        await page.GetByLabel("Cari tes, misalnya CPNS atau BUMN").FillAsync(tag);
        var card = page.Locator($"[data-testid=test-card][data-slug='cpns-{tag}']");
        await Assertions.Expect(card).ToBeVisibleAsync();
        await Assertions.Expect(card.GetByTestId("test-countdown")).ToHaveTextAsync("± 70 hari lagi");
        await Assertions.Expect(card.GetByTestId("test-package")).ToContainTextAsync("Rp50.000");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth"));

        await card.ClickAsync();
        await page.WaitForURLAsync($"**/jadwal-tes/cpns-{tag}");
        await Assertions.Expect(page.GetByTestId("test-days")).ToHaveTextAsync("± 70");
        await Assertions.Expect(page.GetByTestId("package-price")).ToHaveTextAsync("Rp50.000");
        await Assertions.Expect(page.GetByTestId("cta-trial")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("test-outline")).ToContainTextAsync("Nasionalisme");
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth"));

        await page.GetByTestId("cta-trial").ClickAsync();
        await page.WaitForURLAsync($"**/ujian/{exam.Id}/mulai");
    }
}
