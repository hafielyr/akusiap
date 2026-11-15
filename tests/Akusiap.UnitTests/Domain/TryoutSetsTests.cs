using Akusiap.Domain.Sessions;

namespace Akusiap.UnitTests.Domain;

/// <summary>CPNS-07 (D-142): fixed SKD tryout sets of 30 TWK, 35 TIU and 45 TKP with no shared questions.</summary>
public sealed class TryoutSetsTests
{
    [Fact]
    public void Sets_are_filled_in_order_and_only_complete_ones_count()
    {
        var candidates = Bank(twk: 65, tiu: 70, tkp: 100);

        var sets = TryoutSets.Assign(candidates, maxSets: 10);

        Assert.Equal([1, 2], sets.Values.Distinct().Order());
        Assert.Equal(220, sets.Count);
        Assert.Equal(30, sets.Count(s => s.Value == 1 && s.Key < 1000));
        Assert.Equal(35, sets.Count(s => s.Value == 2 && s.Key is >= 1000 and < 2000));
        Assert.Equal(45, sets.Count(s => s.Value == 1 && s.Key >= 2000));
        Assert.Equal(Enumerable.Range(0, 30).Select(i => (long)i), sets.Where(s => s.Value == 1 && s.Key < 1000).Select(s => s.Key).Order()); // best first
    }

    [Fact]
    public void Questions_keep_their_set_and_free_places_are_filled_from_the_rest()
    {
        var first = TryoutSets.Assign(Bank(30, 35, 45), maxSets: 10);
        // One TWK question of set 1 is gone, and new questions arrive (put first, as if better).
        var gone = first.Keys.First(k => k < 1000);
        var again = Bank(31, 35, 45)
            .Where(c => c.QuestionId != gone)
            .Select(c => c with { CurrentSet = first.TryGetValue(c.QuestionId, out var set) ? set : null })
            .OrderBy(c => c.CurrentSet is null ? 0 : 1)
            .ToList();

        var sets = TryoutSets.Assign(again, maxSets: 10);

        Assert.All(first.Where(f => f.Key != gone), f => Assert.Equal(1, sets[f.Key]));
        Assert.Equal(1, sets[30]); // the new TWK question took the free place
        Assert.Equal(110, sets.Count);
    }

    [Fact]
    public void Too_few_questions_make_no_set_and_the_cap_is_respected()
    {
        Assert.Empty(TryoutSets.Assign(Bank(29, 35, 45)));
        Assert.Equal(TryoutSets.MaxSets, TryoutSets.Assign(Bank(400, 400, 500)).Values.Distinct().Count());
    }

    /// <summary>TWK ids 0.., TIU 1000.., TKP 2000.., in preference order.</summary>
    private static List<TryoutCandidate> Bank(int twk, int tiu, int tkp) =>
    [
        .. Enumerable.Range(0, twk).Select(i => new TryoutCandidate(i, SkdSubtest.Twk, null)),
        .. Enumerable.Range(0, tiu).Select(i => new TryoutCandidate(1000 + i, SkdSubtest.Tiu, null)),
        .. Enumerable.Range(0, tkp).Select(i => new TryoutCandidate(2000 + i, SkdSubtest.Tkp, null)),
    ];
}
