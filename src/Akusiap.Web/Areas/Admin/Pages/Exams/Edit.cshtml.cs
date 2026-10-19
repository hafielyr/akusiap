using Akusiap.Application.Exams;
using Akusiap.Application.RulePresets;
using Akusiap.Application.Common;
using Akusiap.Application.Subjects;
using Akusiap.Application.Topics;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Exams;

/// <summary>
/// Edit an app exam (status unchanged), delete one that was never used, and manage its questions (ADM-T4). The question
/// handlers are called by htmx and return only the refreshed panel or search results.
/// </summary>
public sealed class EditModel(
    GetExam getExam,
    SaveExam saveExam,
    DeleteExam deleteExam,
    ListSubjects listSubjects,
    ListRulePresetOptions listPresets,
    GetExamQuestions getExamQuestions,
    SearchQuestionsForExam searchQuestions,
    ChangeExamQuestions changeQuestions,
    ListTopicOptions listTopics,
    IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public ExamInput Input { get; set; } = new();

    public ExamDetail Exam { get; private set; } = null!;

    public ExamFormView Form { get; private set; } = null!;

    public ExamQuestionsView Questions { get; private set; } = null!;

    public IReadOnlyList<QuestionCandidate> Candidates { get; private set; } = [];

    public IReadOnlyList<TopicOption> Topics { get; private set; } = [];

    /// <summary>htmx: Active questions matching the search box and topic filter.</summary>
    public async Task<IActionResult> OnGetSearchAsync(
        [FromQuery(Name = "q")] string? search, [FromQuery(Name = "topik")] long? topicId, CancellationToken cancellationToken)
    {
        if (await getExam.HandleAsync(Id, cancellationToken) is null)
        {
            return NotFound();
        }

        var candidates = await searchQuestions.HandleAsync(Id, search, topicId, cancellationToken);
        return Partial("_QuestionSearchResultsPartial", new QuestionSearchResults(Id, candidates));
    }

    public Task<IActionResult> OnPostAddQuestionAsync(long questionId, CancellationToken cancellationToken) =>
        PanelAsync(changeQuestions.AddAsync(Id, questionId, cancellationToken), addedQuestionId: questionId, cancellationToken);

    public Task<IActionResult> OnPostRemoveQuestionAsync(long questionId, CancellationToken cancellationToken) =>
        PanelAsync(changeQuestions.RemoveAsync(Id, questionId, cancellationToken), addedQuestionId: null, cancellationToken);

    public Task<IActionResult> OnPostMoveQuestionAsync(long questionId, int step, CancellationToken cancellationToken) =>
        PanelAsync(changeQuestions.MoveAsync(Id, questionId, step, cancellationToken), addedQuestionId: null, cancellationToken);

    public Task<IActionResult> OnPostTrialAsync(long questionId, bool isTrial, CancellationToken cancellationToken) =>
        PanelAsync(changeQuestions.SetTrialAsync(Id, questionId, isTrial, cancellationToken), addedQuestionId: null, cancellationToken);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        Input = ExamInput.From(Exam);
        return await ShowAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        ModelState.Clear();
        var (request, priceProblem) = Input.ToRequest(Id);
        var result = priceProblem is null ? await saveExam.HandleAsync(request, cancellationToken) : null;
        if (result is { IsNotFound: true })
        {
            return NotFound();
        }

        if (result is { Succeeded: true })
        {
            TempData["Flash"] = "Admin.Saved";
            return Redirect($"/admin/ujian/{Id}");
        }

        ModelState.AddProblems(priceProblem is null ? result!.Problems : [priceProblem], t, nameof(Input));
        return await LoadAsync(cancellationToken) ? await ShowAsync(cancellationToken) : NotFound();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var result = await deleteExam.HandleAsync(Id, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            if (!await LoadAsync(cancellationToken))
            {
                return NotFound();
            }

            Input = ExamInput.From(Exam);
            return await ShowAsync(cancellationToken);
        }

        TempData["Flash"] = "Admin.Deleted";
        return Redirect("/admin/ujian");
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var exam = await getExam.HandleAsync(Id, cancellationToken);
        if (exam is null)
        {
            return false;
        }

        Exam = exam;
        return true;
    }

    private async Task<PageResult> ShowAsync(CancellationToken cancellationToken)
    {
        Form = new ExamFormView(Input, await listSubjects.OptionsAsync(cancellationToken), await listPresets.HandleAsync(cancellationToken));
        Questions = (await getExamQuestions.HandleAsync(Id, cancellationToken))!;
        Candidates = await searchQuestions.HandleAsync(Id, search: null, topicId: null, cancellationToken);
        Topics = await listTopics.HandleAsync(cancellationToken);
        return Page();
    }

    /// <summary>The refreshed panel; a refused change (for example a duplicate add) keeps the list and shows why.</summary>
    private async Task<IActionResult> PanelAsync(Task<Result<ExamQuestionsView>> change, long? addedQuestionId, CancellationToken cancellationToken)
    {
        var result = await change;
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (result.Succeeded)
        {
            return Partial("_ExamQuestionsPanelPartial", new ExamQuestionsPanel(result.Value!, AddedQuestionId: addedQuestionId));
        }

        var current = await getExamQuestions.HandleAsync(Id, cancellationToken);
        if (current is null)
        {
            return NotFound();
        }

        var message = string.Join(" ", result.Problems.Select(p => ProblemModelState.Describe(p, t)));
        return Partial("_ExamQuestionsPanelPartial", new ExamQuestionsPanel(current, message));
    }
}
