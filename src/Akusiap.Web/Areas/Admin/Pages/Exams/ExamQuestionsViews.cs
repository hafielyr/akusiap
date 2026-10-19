using Akusiap.Application.Exams;

namespace Akusiap.Web.Areas.Admin.Pages.Exams;

/// <summary>Model of <c>_ExamQuestionsPanelPartial</c>: the counters and ordered list, plus an optional message.</summary>
/// <param name="AddedQuestionId">Set after an add, so the matching search result is swapped out-of-band to "already added".</param>
public sealed record ExamQuestionsPanel(ExamQuestionsView View, string? Message = null, long? AddedQuestionId = null);

/// <summary>Model of <c>_QuestionSearchResultsPartial</c>.</summary>
public sealed record QuestionSearchResults(long ExamId, IReadOnlyList<QuestionCandidate> Candidates);
