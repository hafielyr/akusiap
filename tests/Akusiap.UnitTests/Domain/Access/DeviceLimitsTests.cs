using Akusiap.Domain.Access;

namespace Akusiap.UnitTests.Domain.Access;

/// <summary>PAY-13 (D-141): the owner's extra-device prices.</summary>
public sealed class DeviceLimitsTests
{
    [Theory]
    [InlineData(1, 10_000)]
    [InlineData(3, 30_000)]
    [InlineData(6, 60_000)]
    [InlineData(12, 100_000)]
    public void An_extra_device_costs_10k_a_month_or_100k_a_year(int months, long expected) =>
        Assert.Equal(expected, DeviceLimits.ExtraPriceIdr(months));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Up_to_four_extra_devices(int extra, bool valid) => Assert.Equal(valid, DeviceLimits.IsValidExtra(extra));
}
