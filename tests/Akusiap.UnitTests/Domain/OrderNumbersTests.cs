using Akusiap.Domain.Payments;

namespace Akusiap.UnitTests.Domain;

/// <summary>PAY-01: readable order numbers, AKS-yyyyMMdd-XXXX with the Jakarta date.</summary>
public sealed class OrderNumbersTests
{
    [Fact]
    public void Order_numbers_use_the_jakarta_date_and_four_unambiguous_characters()
    {
        var lateEveningUtc = new DateTime(2026, 10, 5, 18, 30, 0, DateTimeKind.Utc); // 01:30 WIB on 6 October

        var number = OrderNumbers.Create(lateEveningUtc, new Random(1));

        Assert.StartsWith("AKS-20261006-", number, StringComparison.Ordinal);
        Assert.Equal(17, number.Length);
        Assert.All(number[13..], c => Assert.Contains(c, OrderNumbers.Alphabet));
        Assert.True(OrderNumbers.IsValid(number));
    }

    [Fact]
    public void A_thousand_numbers_on_one_day_are_almost_always_different()
    {
        var random = new Random(42);
        var now = new DateTime(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc);

        var numbers = Enumerable.Range(0, 1000).Select(_ => OrderNumbers.Create(now, random)).ToList();

        Assert.True(numbers.Distinct().Count() >= 999);
    }

    [Theory]
    [InlineData("AKS-20261006-K7QX", true)]
    [InlineData("AKS-20261006-K7Q0", false)] // 0 is not in the alphabet
    [InlineData("AKS-20261306-K7QX", false)] // month 13
    [InlineData("ABC-20261006-K7QX", false)]
    [InlineData("AKS-20261006-K7Q", false)]
    [InlineData(null, false)]
    public void Validity(string? number, bool valid)
    {
        Assert.Equal(valid, OrderNumbers.IsValid(number));
    }

    [Fact]
    public void The_alphabet_has_no_look_alikes()
    {
        Assert.DoesNotContain('0', OrderNumbers.Alphabet);
        Assert.DoesNotContain('O', OrderNumbers.Alphabet);
        Assert.DoesNotContain('1', OrderNumbers.Alphabet);
        Assert.DoesNotContain('I', OrderNumbers.Alphabet);
        Assert.DoesNotContain('L', OrderNumbers.Alphabet);
    }
}
