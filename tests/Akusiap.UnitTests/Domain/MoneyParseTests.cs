using Akusiap.Domain;

namespace Akusiap.UnitTests.Domain;

/// <summary>ADM-T3: admins type Rupiah prices the Indonesian way.</summary>
public sealed class MoneyParseTests
{
    [Theory]
    [InlineData("49000", 49000)]
    [InlineData("49.000", 49000)]
    [InlineData("Rp49.000", 49000)]
    [InlineData("Rp 49.000", 49000)]
    [InlineData("rp49000", 49000)]
    [InlineData("  1.250.000 ", 1250000)]
    [InlineData("49 000", 49000)]
    [InlineData("0", 0)]
    public void Reads_whole_rupiah_with_or_without_dots_and_prefix(string text, long expected)
    {
        Assert.True(Money.TryParseIdr(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Rp")]
    [InlineData("49.000,50")]
    [InlineData("49,5")]
    [InlineData("-49000")]
    [InlineData("empat puluh")]
    [InlineData("49.00")]
    [InlineData("4.9000")]
    [InlineData("99999999999999999999")]
    public void Rejects_decimals_negatives_words_and_misplaced_dots(string? text)
    {
        Assert.False(Money.TryParseIdr(text, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(49000)]
    [InlineData(1234567)]
    public void Formatting_and_parsing_round_trip(long amount)
    {
        Assert.True(Money.TryParseIdr(Money.FormatIdr(amount), out var parsed));
        Assert.Equal(amount, parsed);
    }
}
