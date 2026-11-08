using Akusiap.Application.ContentPacks;
using Akusiap.Application.Questions;
using Akusiap.Domain.Content;

namespace Akusiap.UnitTests.Application;

/// <summary>CPNS-06: duplicate keys, TKP points from explanations and the quality score.</summary>
public sealed class CpnsPackRulesTests
{
    [Fact]
    public void The_key_ignores_case_spacing_and_punctuation()
    {
        Assert.Equal(CpnsPackRules.Key("Ibu kota Indonesia adalah …"), CpnsPackRules.Key("ibu kota  indonesia adalah..."));
    }

    [Fact]
    public void The_key_keeps_the_shapes_of_figural_questions()
    {
        Assert.NotEqual(CpnsPackRules.Key("Pilih yang berbeda: ▲ ▲ ● ▲"), CpnsPackRules.Key("Pilih yang berbeda: ■ ■ ■ ◆"));
    }

    [Fact]
    public void Points_are_read_from_the_explanation_in_option_order()
    {
        var options = Options(correct: 2);
        var points = CpnsPackRules.PointsFromExplanation("Jawaban terbaik C.\nSkor per pilihan: A 2, B 3, C 5, D 1, E 4.", options);
        Assert.Equal([2, 3, 5, 1, 4], points!.Select(p => p!.Value));
    }

    [Theory]
    [InlineData("Tidak ada skor di sini.")]
    [InlineData("Skor per pilihan: A 2, B 3, C 5, D 1.")] // E missing
    [InlineData("Skor per pilihan: A 5, B 3, C 4, D 1, E 2.")] // right option C is not the best
    [InlineData("Skor per pilihan: A 2, A 3, C 5, D 1, E 4.")] // A twice
    public void Unusable_points_are_ignored(string explanation) =>
        Assert.Null(CpnsPackRules.PointsFromExplanation(explanation, Options(correct: 2)));

    [Fact]
    public void A_good_question_scores_above_the_bar_and_a_weak_one_below()
    {
        var good = Question("Sikap Anda ketika rekan kerja kesulitan memakai aplikasi baru adalah ....", new string('x', 90), Options(correct: 0));
        var weak = Question("Perhatikan gambar berikut!", "Jawab C.", Options(correct: 0, count: 3));

        Assert.True(CpnsPackRules.Score(good, isTkp: false) >= CpnsPackRules.MinScore);
        Assert.True(CpnsPackRules.Score(weak, isTkp: false) < CpnsPackRules.MinScore);
    }

    [Fact]
    public void Tkp_points_and_own_questions_raise_the_score()
    {
        var plain = Question("Sikap Anda ketika antrean panjang menjelang istirahat adalah ....", "Pembahasan singkat.", Options(correct: 0));
        var scored = plain with { Options = [.. plain.Options.Select((o, i) => o with { Points = 5 - i })], Origin = QuestionOrigin.Original };

        Assert.True(CpnsPackRules.Score(scored, isTkp: true) >= CpnsPackRules.Score(plain, isTkp: true) + 3);
    }

    private static List<OptionRequest> Options(int correct, int count = 5) =>
        [.. Enumerable.Range(0, count).Select(i => new OptionRequest(null, $"Pilihan {i}", i == correct))];

    private static SaveQuestionRequest Question(string text, string explanation, List<OptionRequest> options) =>
        new(null, 1, QuestionType.SingleChoice, text, explanation, null, null, null, null, QuestionStatus.Active, options);
}
