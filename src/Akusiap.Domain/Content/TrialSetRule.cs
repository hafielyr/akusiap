namespace Akusiap.Domain.Content;

/// <summary>How many trial questions a paid exam needs: <see cref="Min"/> to <see cref="Max"/>, inclusive.</summary>
public sealed record TrialRequirement(int Min, int Max)
{
    public bool IsMetBy(int trialCount) => trialCount >= Min && trialCount <= Max;
}

/// <summary>
/// D-08: a paid exam needs 20–40 trial questions, or all of its questions when it has fewer than 30. The admin picker
/// shows progress against it (ADM-T4); publishing enforces it (ADM-T6).
/// </summary>
public static class TrialSetRule
{
    public const int SmallExamThreshold = 30;
    public const int MinTrial = 20;
    public const int MaxTrial = 40;

    public static TrialRequirement For(int questionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(questionCount);
        return questionCount < SmallExamThreshold
            ? new TrialRequirement(questionCount, questionCount)
            : new TrialRequirement(MinTrial, Math.Min(MaxTrial, questionCount));
    }
}
