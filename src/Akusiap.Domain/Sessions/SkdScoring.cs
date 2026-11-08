namespace Akusiap.Domain.Sessions;

/// <summary>The three SKD subtests, in the order the CAT test shows them.</summary>
public enum SkdSubtest
{
    Twk,
    Tiu,
    Tkp,
}

/// <summary>One subtest's size and passing grade (CPNS-03, D-130).</summary>
public sealed record SkdSubtestRule(SkdSubtest Subtest, string Code, int QuestionCount, int PassingGrade)
{
    public int MaxScore => QuestionCount * SkdScoring.MaxPointsPerQuestion;
}

/// <summary>One answered (or skipped) question of an SKD attempt.</summary>
/// <param name="PickedPoints">Points of the picked option when the question scores every option (TKP); else null.</param>
/// <param name="HasPoints">Whether the question scores every option.</param>
public sealed record SkdItem(SkdSubtest Subtest, bool IsCorrect, int? PickedPoints, bool HasPoints);

/// <summary>A subtest's score in one attempt.</summary>
public sealed record SkdSubtestScore(SkdSubtestRule Rule, int Questions, int Correct, int Score)
{
    public int MaxScore => Questions * SkdScoring.MaxPointsPerQuestion;

    /// <summary>The score scaled to a full subtest, so a short practice set can be compared with the passing grade.</summary>
    public int Projected => Questions == 0 ? 0 : (int)Math.Round((double)Score * Rule.QuestionCount / Questions, MidpointRounding.AwayFromZero);

    public bool ReachesPassingGrade => Questions > 0 && Projected >= Rule.PassingGrade;

    /// <summary>Points still missing to the passing grade (0 when reached).</summary>
    public int Gap => Math.Max(0, Rule.PassingGrade - Projected);
}

/// <summary>An attempt's SKD score: per subtest, total, and pass or fail when it was a full test.</summary>
public sealed record SkdScore(IReadOnlyList<SkdSubtestScore> Subtests, int Total, int MaxTotal, bool IsFullTest)
{
    /// <summary>Only a full 110-question test passes or fails: every subtest must reach its passing grade.</summary>
    public bool? Passed => IsFullTest ? Subtests.All(s => s.Score >= s.Rule.PassingGrade) : null;
}

/// <summary>
/// The CPNS SKD rules (CPNS-03, D-130), from KepmenPAN-RB No. 321 of 2024 (CPNS 2024), the latest published ones:
/// TWK 30 questions (passing grade 65), TIU 35 (80), TKP 45 (166), 100 minutes. TWK and TIU give 5 points for a right
/// answer and 0 otherwise; TKP gives 1–5 points per option. The 2026 rules are not published yet; change the numbers here
/// when they are.
/// </summary>
public static class SkdScoring
{
    public const int MaxPointsPerQuestion = 5;
    public const int TimeLimitMinutes = 100;

    public static IReadOnlyList<SkdSubtestRule> Rules { get; } =
    [
        new(SkdSubtest.Twk, "TWK", 30, 65),
        new(SkdSubtest.Tiu, "TIU", 35, 80),
        new(SkdSubtest.Tkp, "TKP", 45, 166),
    ];

    public static int QuestionCount => Rules.Sum(r => r.QuestionCount);

    /// <summary>Seconds per question at the real test's pace (100 minutes for 110 questions, about 54).</summary>
    public static int SecondsPerQuestion => TimeLimitMinutes * 60 / QuestionCount;

    public static SkdSubtestRule Rule(SkdSubtest subtest) => Rules.Single(r => r.Subtest == subtest);

    /// <summary>
    /// The subtest a topic belongs to: its name holds the code as a separate word, for example
    /// "Tes Wawasan Kebangsaan (TWK)" or "TIU · Numerik". Null when it holds none or more than one.
    /// </summary>
    public static SkdSubtest? SubtestOf(string? topicName)
    {
        if (string.IsNullOrWhiteSpace(topicName))
        {
            return null;
        }

        var words = topicName.Split([' ', '(', ')', '·', '-', '/', ':', ',', '.'], StringSplitOptions.RemoveEmptyEntries);
        var found = Rules.Where(r => words.Any(w => string.Equals(w, r.Code, StringComparison.OrdinalIgnoreCase))).ToList();
        return found.Count == 1 ? found[0].Subtest : null;
    }

    /// <summary>One question's points: TKP options score themselves; otherwise 5 for right and 0 for wrong or blank.</summary>
    public static int ItemScore(SkdItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Subtest == SkdSubtest.Tkp && item.HasPoints)
        {
            return Math.Clamp(item.PickedPoints ?? 0, 0, MaxPointsPerQuestion);
        }

        return item.IsCorrect ? MaxPointsPerQuestion : 0;
    }

    /// <summary>Scores the items per subtest. A full test has exactly each subtest's question count.</summary>
    public static SkdScore Score(IEnumerable<SkdItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var list = items.ToList();
        var subtests = Rules
            .Select(rule =>
            {
                var mine = list.Where(i => i.Subtest == rule.Subtest).ToList();
                return new SkdSubtestScore(rule, mine.Count, mine.Count(i => i.IsCorrect), mine.Sum(ItemScore));
            })
            .ToList();
        var isFull = subtests.All(s => s.Questions == s.Rule.QuestionCount);
        return new SkdScore(subtests, subtests.Sum(s => s.Score), subtests.Sum(s => s.MaxScore), isFull);
    }

    /// <summary>
    /// Builds a tryout like the real CAT test (CPNS-04): for each subtest in order (TWK, TIU, TKP), its question count
    /// picked at random from the candidates in that subtest. TKP questions with per-option points go first, so the TKP
    /// score uses real points whenever the bank has enough of them. A subtest short of questions gives what it has.
    /// </summary>
    /// <param name="subtestOfTopic">The subtest of each candidate's topic (null: not part of SKD, never picked).</param>
    /// <param name="withPoints">Candidates whose options carry points.</param>
    public static IReadOnlyList<CandidateQuestion> Compose(
        IReadOnlyList<CandidateQuestion> candidates,
        IReadOnlyDictionary<long, SkdSubtest?> subtestOfTopic,
        IReadOnlySet<long> withPoints,
        Random random)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(subtestOfTopic);
        ArgumentNullException.ThrowIfNull(withPoints);
        ArgumentNullException.ThrowIfNull(random);
        var picked = new List<CandidateQuestion>(QuestionCount);
        foreach (var rule in Rules)
        {
            var pool = candidates.Where(c => subtestOfTopic.TryGetValue(c.TopicId, out var s) && s == rule.Subtest).ToList();
            var scored = pool.Where(c => withPoints.Contains(c.QuestionId)).ToList();
            var plain = pool.Where(c => !withPoints.Contains(c.QuestionId)).ToList();
            QuestionSelection.Shuffle(scored, random);
            QuestionSelection.Shuffle(plain, random);
            var ordered = rule.Subtest == SkdSubtest.Tkp ? scored.Concat(plain) : Interleave(scored, plain, random);
            picked.AddRange(ordered.Take(rule.QuestionCount));
        }

        return picked;
    }

    /// <summary>Both lists, already shuffled, merged at random (TWK and TIU don't need points).</summary>
    private static List<CandidateQuestion> Interleave(List<CandidateQuestion> a, List<CandidateQuestion> b, Random random)
    {
        var all = a.Concat(b).ToList();
        QuestionSelection.Shuffle(all, random);
        return all;
    }

    /// <summary>
    /// Whether per-option points are usable: one per option, each 0–5, and the right option has the most points
    /// (so "correct" and "best" agree). All null means a right/wrong question, which is also fine.
    /// </summary>
    public static bool ArePointsConsistent(IReadOnlyList<(bool IsCorrect, int? Points)> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.All(o => o.Points is null))
        {
            return true;
        }

        if (options.Any(o => o.Points is null or < 0 or > MaxPointsPerQuestion))
        {
            return false;
        }

        var best = options.Max(o => o.Points!.Value);
        return options.Where(o => o.IsCorrect).All(o => o.Points == best) && options.Any(o => o.IsCorrect);
    }
}
