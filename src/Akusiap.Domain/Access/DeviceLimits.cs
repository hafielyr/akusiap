namespace Akusiap.Domain.Access;

/// <summary>
/// Devices per subscription (PAY-13, D-141, owner prices 2026-11-14): one device is included; each extra device costs
/// Rp10.000 a month, or Rp100.000 on a 12-month package. Only accounts with an active subscription are limited.
/// </summary>
public static class DeviceLimits
{
    public const int Included = 1;
    public const int MaxExtra = 4;
    public const long ExtraPerMonthIdr = 10_000;
    public const long ExtraPerYearIdr = 100_000;

    /// <summary>The price of one extra device for a package of this many months.</summary>
    public static long ExtraPriceIdr(int durationMonths) =>
        durationMonths == 12 ? ExtraPerYearIdr : ExtraPerMonthIdr * durationMonths;

    public static bool IsValidExtra(int extra) => extra is >= 0 and <= MaxExtra;
}
