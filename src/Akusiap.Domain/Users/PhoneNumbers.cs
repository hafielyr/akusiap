namespace Akusiap.Domain.Users;

/// <summary>
/// Indonesian phone numbers (AC-04, D-48): stored as <c>+62</c> followed by 8–12 digits. People may type spaces, dashes,
/// dots or brackets, and may start with <c>62</c> or the local <c>0</c> instead of <c>+62</c>.
/// </summary>
public static class PhoneNumbers
{
    public const string CountryPrefix = "+62";
    public const int MinDigits = 8;
    public const int MaxDigits = 12;

    /// <returns>True with the <c>+62…</c> form, or false when the input isn't a valid Indonesian number.</returns>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var compact = new string([.. input.Where(c => c is not (' ' or '-' or '.' or '(' or ')'))]);
        string national;
        if (compact.StartsWith(CountryPrefix, StringComparison.Ordinal))
        {
            national = compact[CountryPrefix.Length..];
        }
        else if (compact.StartsWith("62", StringComparison.Ordinal))
        {
            national = compact[2..];
        }
        else if (compact.StartsWith('0'))
        {
            national = compact[1..];
        }
        else
        {
            return false;
        }

        if (national.Length is < MinDigits or > MaxDigits || !national.All(char.IsAsciiDigit) || national[0] == '0')
        {
            return false;
        }

        normalized = CountryPrefix + national;
        return true;
    }
}
