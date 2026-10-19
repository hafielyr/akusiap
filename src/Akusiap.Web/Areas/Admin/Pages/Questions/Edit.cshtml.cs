using Akusiap.Application.Questions;
using Akusiap.Application.Topics;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Questions;

/// <summary>Edit an app question. Saving keeps its id; Retired keeps it in past attempts (ADM-T2).</summary>
public sealed class EditModel(
    GetQuestion getQuestion,
    SaveQuestion saveQuestion,
    ListTopicOptions listTopics,
    IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public QuestionInput Input { get; set; } = new();

    public QuestionDetail Question { get; private set; } = null!;

    public IReadOnlyList<TopicOption> Topics { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        Input = QuestionInput.From(Question);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await saveQuestion.HandleAsync(Input.ToRequest(Id), cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            return await LoadAsync(cancellationToken) ? Page() : NotFound();
        }

        TempData["Flash"] = "Admin.Saved";
        return Redirect($"/admin/soal/{Id}");
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var question = await getQuestion.HandleAsync(Id, cancellationToken);
        if (question is null)
        {
            return false;
        }

        Question = question;
        Topics = await listTopics.HandleAsync(cancellationToken);
        return true;
    }
}
