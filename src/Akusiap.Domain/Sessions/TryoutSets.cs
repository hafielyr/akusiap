namespace Akusiap.Domain.Sessions;

/// <summary>A question that can go in an SKD tryout set, and the set it is in now (null: none).</summary>
public sealed record TryoutCandidate(long QuestionId, SkdSubtest Subtest, int? CurrentSet);

/// <summary>
/// Fixed SKD tryout sets (CPNS-07, D-142, owner request 2026-11-14): up to <see cref="MaxSets"/> sets of 30 TWK, 35 TIU and
/// 45 TKP questions, no question in two sets. Questions keep the set they are in (so a set a learner took stays the same)
/// while it can still be filled; the free places are filled from the other candidates in the order given (best first).
/// Only complete sets are returned.
/// </summary>
public static class TryoutSets
{
    public const int MaxSets = 10;

    /// <summary>Question id → set number (1-based) for the complete sets.</summary>
    public static IReadOnlyDictionary<long, int> Assign(IReadOnlyList<TryoutCandidate> candidates, int maxSets = MaxSets)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var placed = new Dictionary<long, int>();
        var full = Enumerable.Range(1, maxSets).ToHashSet();
        foreach (var rule in SkdScoring.Rules)
        {
            var mine = candidates.Where(c => c.Subtest == rule.Subtest).DistinctBy(c => c.QuestionId).ToList();
            var sets = Enumerable.Range(1, maxSets).ToDictionary(s => s, _ => new List<long>());
            var pool = new List<long>();
            foreach (var candidate in mine)
            {
                if (candidate.CurrentSet is { } set && sets.TryGetValue(set, out var list) && list.Count < rule.QuestionCount)
                {
                    list.Add(candidate.QuestionId);
                }
                else
                {
                    pool.Add(candidate.QuestionId);
                }
            }

            var next = 0;
            for (var set = 1; set <= maxSets; set++)
            {
                while (sets[set].Count < rule.QuestionCount && next < pool.Count)
                {
                    sets[set].Add(pool[next++]);
                }

                if (sets[set].Count < rule.QuestionCount)
                {
                    full.Remove(set);
                }
            }

            foreach (var (set, ids) in sets)
            {
                ids.ForEach(id => placed[id] = set);
            }
        }

        return placed.Where(p => full.Contains(p.Value)).ToDictionary(p => p.Key, p => p.Value);
    }
}
