using System.Globalization;

namespace Akusiap.Domain.Payments;

/// <summary>
/// Readable order numbers such as <c>AKS-20261006-K7QX</c> (PAY-01, D-52): the Jakarta date and four characters from an
/// alphabet without look-alikes (no 0/O, 1/I/L, U), about a million per day. The unique index is the final guard.
/// </summary>
public static class OrderNumbers
{
    public const string Prefix = "AKS";
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int SuffixLength = 4;

    private static readonly System.Buffers.SearchValues<char> AlphabetValues = System.Buffers.SearchValues.Create(Alphabet);

    public static string Create(DateTime utcNow, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var date = JakartaTime.Today(utcNow).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var suffix = new char[SuffixLength];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Alphabet[random.Next(Alphabet.Length)];
        }

        return $"{Prefix}-{date}-{new string(suffix)}";
    }

    public static bool IsValid(string? orderNumber) =>
        orderNumber is { Length: 17 }
        && orderNumber.StartsWith(Prefix + "-", StringComparison.Ordinal)
        && orderNumber[12] == '-'
        && DateOnly.TryParseExact(orderNumber.AsSpan(4, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
        && orderNumber.AsSpan(13).IndexOfAnyExcept(AlphabetValues) < 0;
}
