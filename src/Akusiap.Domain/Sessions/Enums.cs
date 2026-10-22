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

    /// <summary>The daily drill (DRL-01): 10 questions from what the learner can use, due review cards first.</summary>
    DailyDrill,
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
