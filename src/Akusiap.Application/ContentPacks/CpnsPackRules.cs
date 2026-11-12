using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Akusiap.Application.Questions;
using Akusiap.Domain.Content;
using Akusiap.Domain.Sessions;

namespace Akusiap.Application.ContentPacks;

/// <summary>
/// The pure rules behind the CPNS 2026 package (CPNS-06, D-133): how two questions count as the same, how TKP points are
/// read from an explanation, and the quality score that decides which questions go into the package.
/// </summary>
public static partial class CpnsPackRules
{
    public const string SubjectName = "Seleksi CPNS";
    public const string TwkTopic = "Tes Wawasan Kebangsaan (TWK)";
    public const string TiuTopic = "Tes Intelegensia Umum (TIU)";
    public const string TkpTopic = "Tes Karakteristik Pribadi (TKP)";

    /// <summary>A question needs at least this score to be Active and in the package.</summary>
    public const int MinScore = 2;

    public static IReadOnlyList<string> Topics { get; } = [TwkTopic, TiuTopic, TkpTopic];

    /// <summary>
    /// The text two questions are compared by: lower case letters and digits, the shapes and symbols that draw figural
    /// questions (▲, ●, →, ², √), single spaces; ordinary punctuation is dropped. "Ibu kota Indonesia adalah …" and
    /// "ibu kota  indonesia adalah..." match, while two figural questions with the same prompt but different shapes don't.
    /// </summary>
    public static string Key(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text.Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(c) || IsFigure(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space && builder.Length > 0)
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Reads "Skor per pilihan: A 2, B 3, C 5, D 1, E 4" from an explanation into points in option order. Null when the
    /// line is missing, names a letter twice, misses an option or doesn't agree with the right answer.
    /// </summary>
    public static int?[]? PointsFromExplanation(string? explanation, IReadOnlyList<OptionRequest> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrEmpty(explanation) || PointsLine().Match(explanation) is not { Success: true } match)
        {
            return null;
        }

        var points = new int?[options.Count];
        foreach (Match pair in PointsPair().Matches(match.Groups[1].Value))
        {
            var index = char.ToUpperInvariant(pair.Groups[1].Value[0]) - 'A';
            if (index >= options.Count || points[index] is not null)
            {
                return null;
            }

            points[index] = int.Parse(pair.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        return SkdScoring.ArePointsConsistent([.. options.Select((o, i) => (o.IsCorrect, points[i]))]) && points.All(p => p is not null)
            ? points
            : null;
    }

    /// <summary>
    /// How good a question is for the package (higher is better). It rewards a real explanation, five options, TKP
    /// points and the app's own questions, and marks down very short texts and questions that point at a picture the
    /// file doesn't have.
    /// </summary>
    public static int Score(SaveQuestionRequest question, bool isTkp)
    {
        ArgumentNullException.ThrowIfNull(question);
        var score = 0;
        var explanation = TextLength(question.Explanation);
        score += explanation >= 80 ? 2 : explanation >= 30 ? 1 : 0;
        score += question.Options.Count >= 5 ? 1 : question.Options.Count < 4 ? -1 : 0;
        if (isTkp && question.Options.Any(o => o.Points is not null))
        {
            score += 2;
        }

        if (question.Origin == QuestionOrigin.Original)
        {
            score += 1;
        }

        var text = question.Text ?? string.Empty;
        if (TextLength(text) < 15)
        {
            score -= 2;
        }

        if (MissingPicture().IsMatch(text) && !HasDrawing(text))
        {
            score -= 3;
        }

        return score;
    }

    private static int TextLength(string? text) => text?.Trim().Length ?? 0;

    /// <summary>Math symbols, arrows, shapes and the like, which carry meaning in a question; not ordinary punctuation.</summary>
    private static bool IsFigure(char c) =>
        char.GetUnicodeCategory(c) is UnicodeCategory.MathSymbol or UnicodeCategory.OtherSymbol or UnicodeCategory.OtherNumber
        || c is >= '←' and <= '⯿';

    /// <summary>Figural questions in the files are drawn with Unicode shapes or box characters.</summary>
    private static bool HasDrawing(string text) => text.Any(c => c is >= '─' and <= '➿' or >= '←' and <= '⇿');

    [GeneratedRegex(@"Skor per pilihan\s*:\s*((?:[A-Ha-h]\s*[0-5]\s*[,;]?\s*)+)", RegexOptions.CultureInvariant)]
    private static partial Regex PointsLine();

    [GeneratedRegex(@"([A-Ha-h])\s*([0-5])", RegexOptions.CultureInvariant)]
    private static partial Regex PointsPair();

    [GeneratedRegex(@"\b(gambar|grafik|diagram|tabel)\s+(di\s*)?(bawah|berikut|samping|atas)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MissingPicture();
}
