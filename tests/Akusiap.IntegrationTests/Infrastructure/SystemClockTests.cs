using Akusiap.Infrastructure.Time;

namespace Akusiap.IntegrationTests.Infrastructure;

public sealed class SystemClockTests
{
    [Fact]
    public void Returns_utc_time_from_the_time_provider()
    {
        var clock = new SystemClock(new FixedTimeProvider(new DateTimeOffset(2026, 10, 18, 3, 0, 0, TimeSpan.FromHours(7))));

        Assert.Equal(new DateTime(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc), clock.UtcNow);
        Assert.Equal(DateTimeKind.Utc, clock.UtcNow.Kind);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
