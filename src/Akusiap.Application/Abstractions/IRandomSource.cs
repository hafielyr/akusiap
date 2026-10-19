namespace Akusiap.Application.Abstractions;

/// <summary>Randomness for shuffling. Tests pass a seeded <see cref="System.Random"/> so orders are repeatable.</summary>
public interface IRandomSource
{
    Random Random { get; }
}

/// <summary>The production source: the thread-safe shared generator.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    public Random Random => Random.Shared;
}
