using Akusiap.Application.Access;
using Akusiap.Application.OwnQuestions;
using Akusiap.Application.Questions;
using Akusiap.Application.Topics;
using Akusiap.Domain.Access;
using Akusiap.Web.Areas.Admin.Pages.Questions;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Learn.Pages.OwnQuestions;

/// <summary><c>/learn/soal-saya</c> (OWN-03): the learner's own questions and what the allowance still permits.</summary>
public sealed class IndexModel(Application.OwnQuestions.OwnQuestions own, GetAllowance getAllowance) : PageModel
{
    public const string Route = "/learn/soal-saya";

    public IReadOnlyList<OwnQuestionRow> Questions { get; private set; } = [];

    public Allowance Allowance { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Questions = await own.ListAsync(cancellationToken);
        Allowance = await getAllowance.HandleAsync(cancellationToken);
    }
}

/// <summary><c>/learn/soal-saya/baru</c> (OWN-03): the same form as admins, saved as the learner's own question.</summary>
public sealed class CreateModel(
    Application.OwnQuestions.OwnQuestions own,
    ListTopicOptions listTopics,
    GetAllowance getAllowance,
    IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public QuestionInput Input { get; set; } = new();

    public IReadOnlyList<TopicOption> Topics { get; private set; } = [];

    public Allowance Allowance { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Input = QuestionInput.Blank(await own.GeneralTopicIdAsync(cancellationToken));
        Input.Status = Domain.Content.QuestionStatus.Active;
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await own.SaveAsync(Input.ToRequest(null), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            await LoadAsync(cancellationToken);
            return Page();
        }

        TempData["Flash"] = "Own.Questions.Saved";
        return Redirect(IndexModel.Route);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        Topics = await listTopics.HandleAsync(cancellationToken);
        Allowance = await getAllowance.HandleAsync(cancellationToken);
    }
}

/// <summary><c>/learn/soal-saya/{id}</c> (OWN-03): edit or delete an own question.</summary>
public sealed class EditModel(
    Application.OwnQuestions.OwnQuestions own,
    ListTopicOptions listTopics,
    IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public QuestionInput Input { get; set; } = new();

    public IReadOnlyList<TopicOption> Topics { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var question = await own.GetAsync(Id, cancellationToken);
        if (question is null)
        {
            return NotFound();
        }

        Input = QuestionInput.From(question);
        Topics = await listTopics.HandleAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (await own.GetAsync(Id, cancellationToken) is null)
        {
            return NotFound();
        }

        var result = await own.SaveAsync(Input.ToRequest(Id), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            Topics = await listTopics.HandleAsync(cancellationToken);
            return Page();
        }

        TempData["Flash"] = "Own.Questions.Saved";
        return Redirect(IndexModel.Route);
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var result = await own.DeleteAsync(Id, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        TempData["Flash"] = result.Succeeded ? "Own.Questions.Deleted" : Application.OwnQuestions.OwnQuestions.InUse;
        return Redirect(result.Succeeded ? IndexModel.Route : $"{IndexModel.Route}/{Id}");
    }
}
