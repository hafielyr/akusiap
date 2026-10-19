using System.Text.RegularExpressions;
using Akusiap.Application.Sharing;

namespace Akusiap.UnitTests.Sharing;

public sealed partial class ShareTokensTests
{
    [Fact]
    public void Tokens_are_22_url_safe_characters_and_differ()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => ShareTokens.New()).ToList();

        Assert.All(tokens, t => Assert.Matches(UrlSafe(), t));
        Assert.Equal(50, tokens.Distinct(StringComparer.Ordinal).Count());
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{22}$")]
    private static partial Regex UrlSafe();
}
