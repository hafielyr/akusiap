using System.Globalization;

namespace Akusiap.Domain;

/// <summary>Rupiah amounts are whole numbers (long), tax-inclusive, with no decimals.</summary>
public static class Money
{
    /// <summary>Formats an amount the Indonesian way, e.g. 49000 → "Rp49.000", in every UI culture (ID-02).</summary>
    public static string FormatIdr(long amount)
    {
        var digits = Math.Abs(amount).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');
        return amount < 0 ? $"-Rp{digits}" : $"Rp{digits}";
    }
}
