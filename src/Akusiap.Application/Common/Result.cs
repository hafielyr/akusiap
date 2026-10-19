namespace Akusiap.Application.Common;

/// <summary>
/// A failure to show the user. <see cref="Code"/> is a SharedResource key (for example <c>Admin.Topics.InUse</c>), formatted
/// with <see cref="Args"/>; <see cref="Field"/> names the form field, or is null for a page-level message.
/// </summary>
public sealed record Problem(string Code, string? Field = null, params object[] Args)
{
    public const string NotFoundCode = "NotFound";

    public static Problem NotFound { get; } = new(NotFoundCode);
}

/// <summary>The outcome of a use case: a value, or one or more problems. Create it with <see cref="Result"/>.</summary>
public sealed record Result<T>
{
    internal Result(T? value, IReadOnlyList<Problem> problems)
    {
        Value = value;
        Problems = problems;
    }

    public T? Value { get; }

    public IReadOnlyList<Problem> Problems { get; }

    public bool Succeeded => Problems.Count == 0;

    public bool IsNotFound => Problems.Any(p => p.Code == Problem.NotFoundCode);
}

public static class Result
{
    public static Result<T> Ok<T>(T value) => new(value, []);

    public static Result<T> Fail<T>(params Problem[] problems) => new(default, problems);

    public static Result<T> Fail<T>(IReadOnlyList<Problem> problems) => new(default, problems);
}
