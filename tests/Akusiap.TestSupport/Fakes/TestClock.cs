using Akusiap.Application.Abstractions;

namespace Akusiap.TestSupport.Fakes;

/// <summary>A clock tests can set and move forward.</summary>
public sealed class TestClock : IClock
{
    public static readonly DateTime DefaultStart = new(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc);

    private DateTime _utcNow = DefaultStart;

    public DateTime UtcNow
    {
        get => _utcNow;
        set => _utcNow = value.Kind == DateTimeKind.Utc
            ? value
            : throw new ArgumentException("Use a UTC time.", nameof(value));
    }

    public void Advance(TimeSpan by) => _utcNow += by;
}
