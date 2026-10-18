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
