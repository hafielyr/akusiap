namespace Akusiap.Domain.Content;

public enum OwnerKind
{
    App,
    Teacher,
    Learner,
}

public enum ExamStatus
{
    Draft,
    Published,
    Unpublished,
    TakenDown,
}

public enum QuestionType
{
    SingleChoice,
    MultiSelect,
    TrueFalse,
}

public enum QuestionStatus
{
    Draft,
    Active,
    Retired,
}

public enum ShowAnswers
{
    AfterEach,
    AfterSubmit,
    Never,
}

/// <summary>Who wrote a question (ADM-T8): the app's own team, or someone else (a book, a site, a repo).</summary>
public enum QuestionOrigin
{
    Original,
    ThirdParty,
}

/// <summary>
/// How an exam is scored (CPNS-03, D-130). Standard: percent correct. Skd: the CPNS SKD rules in
/// <see cref="Sessions.SkdScoring"/> (TWK, TIU and TKP subtests, each with its passing grade).
/// </summary>
public enum ScoringScheme
{
    Standard,
    Skd,
}
