using Akusiap.Application.Abstractions;

namespace Akusiap.Infrastructure.Time;

internal sealed class SystemClock(TimeProvider timeProvider) : IClock
{
    public DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;
}
