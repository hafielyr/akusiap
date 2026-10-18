using Akusiap.Domain;

namespace Akusiap.UnitTests.Domain;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(0, "Rp0")]
    [InlineData(1000, "Rp1.000")]
    [InlineData(49000, "Rp49.000")]
    [InlineData(1234567, "Rp1.234.567")]
    [InlineData(-25000, "-Rp25.000")]
    public void FormatIdr_uses_dot_thousands_separator_and_no_decimals(long amount, string expected)
    {
        Assert.Equal(expected, Money.FormatIdr(amount));
    }

    [Fact]
    public void FormatIdr_ignores_current_culture()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal("Rp49.000", Money.FormatIdr(49000));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
