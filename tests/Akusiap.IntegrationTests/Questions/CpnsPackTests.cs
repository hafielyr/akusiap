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
/// CPNS-06: loading the question files skips duplicates and broken rows, reads TKP points, keeps weak questions as Draft
/// and builds the published Rp50.000 SKD package with its trial set and the CPNS 2026 test page link.
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
