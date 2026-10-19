using Akusiap.Application.Subjects;
using Akusiap.Application.Topics;
using Akusiap.Web.Areas.Admin.Pages.Subjects;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Topics;

/// <summary>Edit a topic (name, description, subject) and delete it when no question uses it.</summary>
public sealed class EditModel(
    GetTopic getTopic,
    SaveTopic saveTopic,
    DeleteTopic deleteTopic,
    ListSubjects listSubjects,
    IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public TopicInput Input { get; set; } = new();

    public TopicDetail Topic { get; private set; } = null!;

    public IReadOnlyList<SubjectOption> SubjectOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        Input = new TopicInput { SubjectId = Topic.SubjectId, Name = Topic.Name, Description = Topic.Description };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await saveTopic.HandleAsync(new SaveTopicRequest(Id, Input.SubjectId, Input.Name, Input.Description), cancellationToken);
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
        return Redirect($"/admin/subjek/{Input.SubjectId}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var result = await deleteTopic.HandleAsync(Id, cancellationToken);
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

            Input = new TopicInput { SubjectId = Topic.SubjectId, Name = Topic.Name, Description = Topic.Description };
            return Page();
        }

        TempData["Flash"] = "Admin.Deleted";
        return Redirect($"/admin/subjek/{result.Value}");
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var topic = await getTopic.HandleAsync(Id, cancellationToken);
        if (topic is null)
        {
            return false;
        }

        Topic = topic;
        SubjectOptions = await listSubjects.OptionsAsync(cancellationToken);
        return true;
    }
}
