using Akusiap.Application.Catalog;

namespace Akusiap.UnitTests.Application;

/// <summary>BRD-04: question search shows a short piece of the question around the match, never the whole text.</summary>
public sealed class SearchSnippetTests
{
    [Fact]
    public void A_short_question_is_shown_whole_with_the_match_marked()
    {
        var snippet = SearchSnippet.Around("Siapa  perumus\nPancasila?", "pancasila", 160);

        Assert.Equal("Siapa perumus Pancasila?", snippet.Text);
        Assert.Equal("Pancasila", snippet.Text.Substring(snippet.MatchStart, snippet.MatchLength));
    }

    [Fact]
    public void A_long_question_is_cut_around_the_match_with_ellipses()
    {
        var text = string.Join(' ', Enumerable.Repeat("kata", 60)) + " deret angka berikut " + string.Join(' ', Enumerable.Repeat("lagi", 60));

        var snippet = SearchSnippet.Around(text, "DERET ANGKA", 160);

        Assert.True(snippet.Text.Length <= 162, $"Snippet is {snippet.Text.Length} chars");
        Assert.StartsWith("…", snippet.Text, StringComparison.Ordinal);
        Assert.EndsWith("…", snippet.Text, StringComparison.Ordinal);
        Assert.Equal("deret angka", snippet.Text.Substring(snippet.MatchStart, snippet.MatchLength));
    }

    [Fact]
    public void Without_a_match_the_start_of_the_question_is_shown()
    {
        var snippet = SearchSnippet.Around(new string('a', 300), "zzz", 160);

        Assert.Equal(161, snippet.Text.Length);
        Assert.EndsWith("…", snippet.Text, StringComparison.Ordinal);
        Assert.Equal(0, snippet.MatchLength);
    }
}
