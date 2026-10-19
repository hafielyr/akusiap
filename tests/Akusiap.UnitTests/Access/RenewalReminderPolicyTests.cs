using Akusiap.Domain.Access;

namespace Akusiap.UnitTests.Access;

public sealed class RenewalReminderPolicyTests
{
    // 09:00 WIB on 10 Oct 2026.
    private static readonly DateTime Now = new(2026, 10, 10, 2, 0, 0, DateTimeKind.Utc);

    private static DateTime EndInJakartaDays(int days) => Now.AddDays(days).AddHours(5); // 14:00 WIB that day

    [Theory]
    [InlineData(8, RenewalReminder.None)]
    [InlineData(7, RenewalReminder.SevenDays)]
    [InlineData(3, RenewalReminder.SevenDays)]
    [InlineData(2, RenewalReminder.SevenDays)]
    [InlineData(1, RenewalReminder.OneDay)]
    [InlineData(0, RenewalReminder.OneDay)]
    public void Nothing_sent_yet(int days, RenewalReminder expected) =>
        Assert.Equal(expected, RenewalReminderPolicy.Due(EndInJakartaDays(days), Now, null, null));

    [Fact]
    public void Each_reminder_is_due_once()
    {
        Assert.Equal(RenewalReminder.None, RenewalReminderPolicy.Due(EndInJakartaDays(5), Now, Now.AddDays(-2), null));
        Assert.Equal(RenewalReminder.None, RenewalReminderPolicy.Due(EndInJakartaDays(1), Now, Now.AddDays(-6), Now.AddHours(-1)));
        Assert.Equal(RenewalReminder.OneDay, RenewalReminderPolicy.Due(EndInJakartaDays(1), Now, Now.AddDays(-6), null));
    }

    [Fact]
    public void Ended_packages_get_nothing() =>
        Assert.Equal(RenewalReminder.None, RenewalReminderPolicy.Due(Now.AddMinutes(-1), Now, null, null));

    [Fact]
    public void Days_are_counted_in_Jakarta()
    {
        // 01:00 WIB on 18 Oct is 18:00 UTC on 17 Oct: 8 Jakarta days away, so not yet.
        var end = new DateTime(2026, 10, 17, 18, 0, 0, DateTimeKind.Utc);
        Assert.Equal(RenewalReminder.None, RenewalReminderPolicy.Due(end, Now, null, null));
        Assert.Equal(RenewalReminder.SevenDays, RenewalReminderPolicy.Due(end, Now.AddDays(1), null, null));
    }
}
