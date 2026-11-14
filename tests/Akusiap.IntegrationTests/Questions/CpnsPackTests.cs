using System.Text;
using Akusiap.Application.ContentPacks;
using Akusiap.Domain.Content;
using Akusiap.TestSupport.Builders;
using Akusiap.TestSupport.Database;
using Akusiap.TestSupport.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Akusiap.IntegrationTests.Questions;

/// <summary>
/// CPNS-06, CPNS-07: loading the question files skips duplicates and broken rows, reads TKP points, keeps weak questions
/// as Draft and builds the published Rp39.000 SKD package with fixed tryout sets, its trial set and the CPNS 2026 test
/// page link.
/// </summary>
[Collection(UsesDatabase.Name)]
public sealed class CpnsPackTests(PostgresFixture database)
{
    private const string Header = "type,text,option_a,option_b,option_c,option_d,option_e,correct,explanation,topic,origin";

    [Fact]
    public async Task Loading_imports_new_questions_and_builds_the_package()
    {
        await using var app = new AppFactory(database);
        var tag = TestData.Unique();
        var twk = $"Sila pertama Pancasila {tag} berbunyi ....";
        var explanation = "Sila pertama berbunyi Ketuhanan Yang Maha Esa, sesuai Pembukaan UUD 1945 alinea keempat yang menjadi dasar negara.";
        var first = Csv(
            Row("TWK", twk, "C", explanation),
            Row("TWK", twk.ToUpperInvariant().Replace("....", "…", StringComparison.Ordinal), "C", explanation),
            Row("TKP", $"Sikap Anda saat antrean {tag} panjang menjelang istirahat adalah ....", "C", "Layanan tetap jalan.\nSkor per pilihan: A 2, B 3, C 5, D 1, E 4."),
            Row("TIU", $"Perhatikan gambar berikut! {tag}", "A", "Jawab A.", options: 3),
            Row("TIU", $"Soal tanpa kunci {tag}", string.Empty, explanation));
        var second = Csv(Row("TWK", twk, "C", explanation));

        PackReport report;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            report = await scope.ServiceProvider.GetRequiredService<CpnsPack>().LoadAsync(
                [new PackFile("a.csv", first), new PackFile("b.csv", second)], CancellationToken.None);
        }

        var (a, b) = (report.Files[0], report.Files[1]);
        Assert.Equal((5, 3, 1, 1), (a.Rows, a.Added, a.Duplicates, a.Invalid));
        Assert.Equal((0, 1), (b.Added, b.Duplicates));

        await using var db = database.CreateContext();
        var exam = await db.Exams.SingleAsync(e => e.Id == report.ExamId);
        Assert.Equal((CpnsPack.PriceIdr, CpnsPack.AccessDays, ScoringScheme.Skd, ExamStatus.Published, false), (exam.PriceIdr!.Value, exam.AccessDays!.Value, exam.ScoringScheme, exam.Status, exam.IsFree));

        var tkp = await db.Questions.Include(q => q.Options).SingleAsync(q => q.Text.Contains("antrean " + tag));
        Assert.Equal([2, 3, 5, 1, 4], tkp.Options.OrderBy(o => o.SortOrder).Select(o => o.Points!.Value));
        var weak = await db.Questions.SingleAsync(q => q.Text.Contains("gambar berikut! " + tag));
        Assert.Equal(QuestionStatus.Draft, weak.Status);
        var inExam = await db.ExamQuestions.Where(x => x.ExamId == exam.Id).Select(x => x.QuestionId).ToListAsync();
        Assert.Contains(tkp.Id, inExam);
        Assert.DoesNotContain(weak.Id, inExam);
        Assert.True(await db.ExamQuestions.AnyAsync(x => x.ExamId == exam.Id && x.IsTrial));

        var cpns = await db.UpcomingTests.SingleAsync(t => t.Slug == UpcomingTestSeed.CpnsSlug);
        Assert.Equal(exam.Id, cpns.ExamId);
        Assert.True(cpns.IsPublished);
    }

    [Fact]
    public async Task Enough_questions_make_fixed_tryout_sets_without_shared_questions_that_stay_put_and_can_be_taken()
    {
        await using var app = new AppFactory(database);
        await app.SeedReferenceDataAsync();
        var tag = TestData.Unique();
        const string explanation = "Pembahasan lengkap: jawaban yang tepat sesuai materi dasar SKD dan aturan yang berlaku, dijelaskan langkah demi langkah.";
        const string tkpExplanation = "Sikap terbaik mengutamakan pelayanan dan tanggung jawab kepada masyarakat.\nSkor per pilihan: A 2, B 3, C 5, D 1, E 4.";
        var rows = new List<string>();
        rows.AddRange(Enumerable.Range(1, 60).Select(i => Row("TWK", $"Soal wawasan kebangsaan nomor {i} paket {tag} adalah ....", "C", explanation)));
        rows.AddRange(Enumerable.Range(1, 70).Select(i => Row("TIU", $"Soal intelegensia umum nomor {i} paket {tag} adalah ....", "A", explanation)));
        rows.AddRange(Enumerable.Range(1, 90).Select(i => Row("TKP", $"Soal karakteristik pribadi nomor {i} paket {tag} adalah ....", "C", tkpExplanation)));

        PackReport report;
        await using (var scope = app.Services.CreateAsyncScope())
        {
            report = await scope.ServiceProvider.GetRequiredService<CpnsPack>().LoadAsync([new PackFile("sets.csv", Csv([.. rows]))], CancellationToken.None);
        }

        await using var db = database.CreateContext();
        var exam = await db.Exams.SingleAsync(e => e.Id == report.ExamId);
        var links = await db.ExamQuestions.Where(x => x.ExamId == exam.Id).Select(x => new { x.QuestionId, x.TryoutSet, Topic = x.Question!.Topic!.Name }).ToListAsync();
        Assert.Equal(2, report.TryoutSets);
        Assert.Equal((39_000L, 80_000L, 90), (exam.PriceIdr!.Value, exam.OriginalPriceIdr!.Value, exam.AccessDays!.Value));
        Assert.Equal(220, links.Count);
        Assert.All(links, l => Assert.NotNull(l.TryoutSet));
        foreach (var set in new short[] { 1, 2 })
        {
            Assert.Equal(
                [(CpnsPackRules.TiuTopic, 35), (CpnsPackRules.TkpTopic, 45), (CpnsPackRules.TwkTopic, 30)],
                links.Where(l => l.TryoutSet == set).GroupBy(l => l.Topic).Select(g => (g.Key, g.Count())).OrderBy(g => g.Key));
        }

        Assert.StartsWith("220 soal dengan pembahasan dan kunci jawaban (TWK 60, TIU 70, TKP 90)\n2 paket tryout", exam.Description, StringComparison.Ordinal);
        Assert.DoesNotContain("KemenPAN", exam.Description, StringComparison.OrdinalIgnoreCase);

        // Loading again keeps every question in its set.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CpnsPack>().LoadAsync([], CancellationToken.None);
        }

        await using (var again = database.CreateContext())
        {
            var after = await again.ExamQuestions.Where(x => x.ExamId == exam.Id).ToDictionaryAsync(x => x.QuestionId, x => x.TryoutSet);
            Assert.All(links, l => Assert.Equal(l.TryoutSet, after[l.QuestionId]));
        }

        // A buyer picks a set on the setup page and gets exactly its 110 questions, TWK, then TIU, then TKP.
        var user = TestData.User().Build();
        using var buyer = await app.CreateSignedInClientAsync(user, Akusiap.Domain.Users.RoleNames.Learner);
        db.ExamPurchases.Add(new Akusiap.Domain.Access.ExamPurchase
        {
            UserId = user.Id, ExamId = exam.Id, PricePaidIdr = 39_000, Status = Akusiap.Domain.Access.ExamPurchaseStatus.Active,
            PurchasedAt = TestData.Now.AddDays(-1), AccessExpiresAt = TestData.Now.AddDays(89),
        });
        await db.SaveChangesAsync();
        var setup = await buyer.GetStringAsync(new Uri($"/ujian/{exam.Id}/mulai?mode=Simulation", UriKind.Relative));
        using var started = await buyer.PostFormAsync($"/ujian/{exam.Id}/mulai", new Dictionary<string, string> { ["Form.Mode"] = "Simulation", ["Form.TryoutSet"] = "2" });

        Assert.Contains("data-testid=\"tryout-set\" data-number=\"1\" data-done=\"false\"", setup, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"tryout-set\" data-number=\"2\"", setup, StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.Redirect, started.StatusCode);
        var attemptId = long.Parse(started.Headers.Location!.OriginalString.Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture);
        var items = await db.AttemptItems.Where(i => i.AttemptId == attemptId).OrderBy(i => i.Position).Select(i => new { i.QuestionId, Topic = i.Question!.Topic!.Name }).ToListAsync();
        var setTwo = links.Where(l => l.TryoutSet == 2).Select(l => l.QuestionId).ToHashSet();
        Assert.Equal(110, items.Count);
        Assert.All(items, i => Assert.Contains(i.QuestionId, setTwo));
        Assert.Equal(
            [.. Enumerable.Repeat(CpnsPackRules.TwkTopic, 30), .. Enumerable.Repeat(CpnsPackRules.TiuTopic, 35), .. Enumerable.Repeat(CpnsPackRules.TkpTopic, 45)],
            items.Select(i => i.Topic));
    }

    private static MemoryStream Csv(params string[] rows) =>
        new(Encoding.UTF8.GetBytes(string.Join('\n', [Header, .. rows])));

    private static string Row(string subtest, string text, string correct, string explanation, int options = 5)
    {
        var topic = subtest switch
        {
            "TWK" => CpnsPackRules.TwkTopic,
            "TIU" => CpnsPackRules.TiuTopic,
            _ => CpnsPackRules.TkpTopic,
        };
        var cells = Enumerable.Range(0, 5).Select(i => i < options ? $"Pilihan {i + 1} {subtest}" : string.Empty);
        string[] values = ["SingleChoice", text, .. cells, correct, explanation, $"{CpnsPackRules.SubjectName} / {topic}", "Sendiri"];
        return string.Join(',', values.Select(Quote));
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
