using Akusiap.Domain.Users;

namespace Akusiap.UnitTests.Domain;

/// <summary>AC-04: phone numbers are +62 followed by 8–12 digits.</summary>
public sealed class PhoneNumbersTests
{
    [Theory]
    [InlineData("+6281234567890", "+6281234567890")]
    [InlineData("+62 812-3456-7890", "+6281234567890")]
    [InlineData("6281234567890", "+6281234567890")]
    [InlineData("081234567890", "+6281234567890")]
    [InlineData("(021) 1234.5678", "+622112345678")]
    [InlineData("+6281234567", "+6281234567")]
    [InlineData("+62812345678901", "+62812345678901")]
    public void Valid_numbers_are_stored_with_plus_62(string input, string expected)
    {
        Assert.True(PhoneNumbers.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+621234567")] // 7 digits
    [InlineData("+628123456789012")] // 13 digits
    [InlineData("+6508123456789")] // another country
    [InlineData("812345678")] // no prefix
    [InlineData("+62 812 abc 4567")]
    [InlineData("+620812345678")] // national part may not start with 0
    [InlineData("00812345678")]
    public void Invalid_numbers_are_refused(string input)
    {
        Assert.False(PhoneNumbers.TryNormalize(input, out _));
    }
}
