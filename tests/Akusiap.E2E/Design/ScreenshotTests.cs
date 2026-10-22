using Akusiap.Domain.Memorization;
using Akusiap.Domain.Users;
using Akusiap.TestSupport.Builders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace Akusiap.E2E.Design;

/// <summary>
/// BRD-01: full-page screenshots of the main screens on a phone and a desktop, for design review. Runs only when
/// LS_SCREENSHOTS_DIR is set (the PNGs go there); otherwise it does nothing, so CI stays fast.
/// </summary>
[Collection(UsesMobileSite.Name)]
public sealed class ScreenshotTests(MobileSite site)
{
    public const string DirectoryVariable = "LS_SCREENSHOTS_DIR";

    private static readonly string[] RivalNames = ["Andi Pratama", "Budi Santoso", "Citra Lestari", "Dewi Anggraini", "Eko Saputra", "Fajar Nugroho"];

    [Fact]
    public async Task Capture_main_screens()
    {
        var directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        var learner = await site.CreateUserAsync(TestData.User().Named("Sari").Build(), "layar-sandi-2026", RoleNames.Learner);
        var exams = new List<long>();
        foreach (var (title, free) in new[] { ("Tryout SKD CPNS 2026", false), ("Latihan TIU Dasar", true), ("Rekrutmen Bersama BUMN: TKD", false) })
        {
            var builder = TestData.Exam().Titled($"{title} {TestData.Unique()[..4]}").WithQuestions(12, trial: free ? 0 : 10);
            exams.Add((await site.SaveExamAsync(free ? builder.Free() : builder.Paid(79_000))).Id);
        }

        // GAM-01 screens: a leaderboard with rivals, the learner's journey, and a teacher's class.
        var teacher = await site.CreateUserAsync(TestData.User().Named("Bu Rina").Build(), "layar-sandi-2026", RoleNames.Learner, RoleNames.Teacher);
        var teacherExam = await site.SaveExamAsync(TestData.Exam().Titled("Latihan TWK Kelas XII").OwnedBy(teacher).WithQuestions(10));
        long classId = 0;
        await site.WithDatabaseAsync(async db =>
        {
            var question = db.ExamQuestions.Where(q => q.ExamId == exams[1]).Select(q => q.QuestionId).First();
            db.ReviewCards.Add(new ReviewCard { UserId = learner.Id, QuestionId = question, State = ReviewCardState.Learning, DueAt = site.Now.AddHours(-1) });
            var names = RivalNames;
            for (var i = 0; i < names.Length; i++)
            {
                var rival = new Akusiap.Domain.Users.User { UserName = $"r{i}-{TestData.Unique()}@contoh.id", Email = $"r{i}-{TestData.Unique()}@contoh.id", DisplayName = names[i], CreatedAt = site.Now, HideOnLeaderboard = i == 3 };
                db.Users.Add(rival);
                db.Attempts.Add(Finished(rival, exams[1], 95m - (i * 7), 900 + (i * 60), site.Now.AddHours(-i - 2)));
            }

            var me = await db.Users.SingleAsync(u => u.Id == learner.Id);
            db.Attempts.Add(Finished(me, exams[1], 80m, 1080, site.Now.AddDays(-1)));
            db.Attempts.Add(Finished(me, exams[1], 70m, 1200, site.Now.AddDays(-2)));
            var classroom = new Akusiap.Domain.Classes.Classroom { TeacherUserId = teacher.Id, Name = "Bimbel SKD Batch 3", InviteCode = "K7QX2M9P", CreatedAt = site.Now };
            classroom.Members.Add(new Akusiap.Domain.Classes.ClassMember { UserId = learner.Id, JoinedAt = site.Now });
            db.Classrooms.Add(classroom);
            db.ClassAssignments.Add(new Akusiap.Domain.Classes.ClassAssignment { Classroom = classroom, ExamId = teacherExam.Id, RulePresetId = teacherExam.RulePresetId, OpensAt = site.Now.AddHours(-1), ClosesAt = site.Now.AddDays(6) });
            await db.SaveChangesAsync();
            classId = classroom.Id;
        });

        foreach (var (name, desktop) in new[] { ("phone", false), ("desktop", true) })
        {
            await using var device = desktop ? await site.NewDesktopAsync() : await site.NewPhoneAsync();
            var page = await device.NewPageAsync();
            await ShotAsync(page, "/", $"{name}-1-home");
            await ShotAsync(page, "/ujian", $"{name}-2-catalog");
            await ShotAsync(page, $"/ujian/{exams[0]}", $"{name}-3-exam");
            await ShotAsync(page, "/akun/masuk", $"{name}-4-sign-in");
            await page.GetByLabel("Email").FillAsync(learner.Email!);
            await page.GetByLabel("Kata sandi").FillAsync("layar-sandi-2026");
            await page.Locator("form button[type=submit]").ClickAsync();
            await page.WaitForURLAsync(url => !url.Contains("/akun/masuk", StringComparison.Ordinal));
            await ShotAsync(page, "/learn", $"{name}-5-learn");
            await ShotAsync(page, "/learn/hari-ini", $"{name}-6-today");
            await ShotAsync(page, "/learn/perjalanan", $"{name}-7-journey");
            await ShotAsync(page, $"/ujian/{exams[1]}/peringkat", $"{name}-8-leaderboard");
            await ShotAsync(page, $"/learn/kelas/{classId}", $"{name}-9-class-student");

            await using var teacherDevice = desktop ? await site.NewDesktopAsync() : await site.NewPhoneAsync();
            var teacherPage = await teacherDevice.NewPageAsync();
            await teacherPage.GotoAsync("/akun/masuk");
            await teacherPage.GetByLabel("Email").FillAsync(teacher.Email!);
            await teacherPage.GetByLabel("Kata sandi").FillAsync("layar-sandi-2026");
            await teacherPage.Locator("form button[type=submit]").ClickAsync();
            await teacherPage.WaitForURLAsync(url => !url.Contains("/akun/masuk", StringComparison.Ordinal));
            await ShotAsync(teacherPage, $"/learn/kelas/{classId}", $"{name}-10-class-teacher");
        }

        static Akusiap.Domain.Sessions.Attempt Finished(Akusiap.Domain.Users.User user, long examId, decimal score, int seconds, DateTime at) => new()
        {
            User = user, ExamId = examId, Source = Akusiap.Domain.Sessions.AttemptSource.Exam, Mode = Akusiap.Domain.Sessions.AttemptMode.Simulation,
            Status = Akusiap.Domain.Sessions.AttemptStatus.Submitted, RulesSnapshot = "{}", StartedAt = at.AddSeconds(-seconds), LastSavedAt = at, SubmittedAt = at,
            ScorePercent = score, TimeSpentSeconds = seconds, CorrectCount = (int)(score / 10), TotalCount = 10,
        };

        async Task ShotAsync(IPage page, string url, string file)
        {
            await page.GotoAsync(url);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(directory, $"{file}.png"), FullPage = true });
        }
    }
}
