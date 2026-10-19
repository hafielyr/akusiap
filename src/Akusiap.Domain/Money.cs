using System.Globalization;
using System.Text.RegularExpressions;

namespace Akusiap.Domain;

/// <summary>Rupiah amounts are whole numbers (long), tax-inclusive, with no decimals.</summary>
public static partial class Money
{
    /// <summary>Formats an amount the Indonesian way, e.g. 49000 → "Rp49.000", in every UI culture (ID-02).</summary>
    public static string FormatIdr(long amount)
    {
        var digits = Math.Abs(amount).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');
        return amount < 0 ? $"-Rp{digits}" : $"Rp{digits}";
    }

    /// <summary>
    /// Reads a whole, non-negative Rupiah amount as people type it: "49000", "49.000", "Rp49.000", "Rp 49 000".
    /// Dots are only accepted as thousand separators; decimals, commas and negative amounts are rejected.
    /// </summary>
    public static bool TryParseIdr(string? text, out long amount)
    {
        amount = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith("rp", StringComparison.OrdinalIgnoreCase))
        {
            value = value[2..];
        }

        value = value.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (!PlainDigits().IsMatch(value) && !ThousandsGrouped().IsMatch(value))
        {
            return false;
        }

        return long.TryParse(value.Replace(".", string.Empty, StringComparison.Ordinal), NumberStyles.None, CultureInfo.InvariantCulture, out amount);
    }

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex PlainDigits();

    [GeneratedRegex(@"^\d{1,3}(\.\d{3})+$")]
    private static partial Regex ThousandsGrouped();
}
