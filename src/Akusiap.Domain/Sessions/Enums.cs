namespace Akusiap.Domain.Sessions;

/// <summary>What the attempt was built from.</summary>
public enum AttemptSource
{
    Exam,
    Trial,
    Shared,
    WrongOnly,
    Review,
    CustomMix,
}

public enum AttemptMode
{
    Practice,
    Simulation,
    Flashcard,
}

public enum AttemptStatus
{
    InProgress,
    Submitted,
    Expired,
}
