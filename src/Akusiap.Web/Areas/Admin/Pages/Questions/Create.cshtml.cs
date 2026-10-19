using Akusiap.Application.Questions;
using Akusiap.Application.Topics;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Questions;

public sealed class CreateModel(SaveQuestion saveQuestion, ListTopicOptions listTopics, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public QuestionInput Input { get; set; } = new();

    public IReadOnlyList<TopicOption> Topics { get; private set; } = [];

    /// <summary>Optional topic to preselect (<c>?topik=</c>).</summary>
    public async Task OnGetAsync([FromQuery(Name = "topik")] long? topicId, CancellationToken cancellationToken)
    {
        Topics = await listTopics.HandleAsync(cancellationToken);
        Input = QuestionInput.Blank(topicId ?? (Topics.Count > 0 ? Topics[0].Id : 0));
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await saveQuestion.HandleAsync(Input.ToRequest(null), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            Topics = await listTopics.HandleAsync(cancellationToken);
            return Page();
        }

        TempData["Flash"] = "Admin.Saved";
        return Redirect($"/admin/soal/{result.Value}");
    }
}
