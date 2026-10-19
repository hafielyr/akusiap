using Akusiap.Application.Abstractions;

namespace Akusiap.TestSupport.Fakes;

/// <summary>A fixed viewer for use-case tests that run without an HTTP request.</summary>
public sealed record FakeCurrentUser(Guid? UserId, bool IsAdmin = false) : ICurrentUser
{
    public static FakeCurrentUser Guest { get; } = new(UserId: null);
}
