namespace Akusiap.Application.Subjects;

public sealed record SubjectSummary(long Id, string Name, string? Description, int TopicCount, int ExamCount);

public sealed record TopicSummary(long Id, string Name, string? Description, int QuestionCount);

public sealed record SubjectDetail(long Id, string Name, string? Description, int ExamCount, IReadOnlyList<TopicSummary> Topics);

/// <summary>For dropdowns.</summary>
public sealed record SubjectOption(long Id, string Name);
