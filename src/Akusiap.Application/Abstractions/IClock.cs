namespace Akusiap.Application.Abstractions;

/// <summary>The only source of the current time. Inject it instead of reading <c>DateTime.UtcNow</c>.</summary>
public interface IClock
{
    /// <summary>The current time in UTC (<see cref="DateTimeKind.Utc"/>).</summary>
    DateTime UtcNow { get; }
}
