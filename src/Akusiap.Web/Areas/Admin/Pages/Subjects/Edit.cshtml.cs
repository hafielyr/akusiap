using Akusiap.Application.Subjects;
using Akusiap.Application.Topics;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.Subjects;

/// <summary>Edit a subject, list and add its topics, and delete it when nothing uses it.</summary>
public sealed class EditModel(
    GetSubject getSubject,
    SaveSubject saveSubject,
    DeleteSubject deleteSubject,
    SaveTopic saveTopic,
    IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long Id { get; set; }

    [BindProperty]
    public SubjectInput Input { get; set; } = new();

    [BindProperty]
    public TopicInput NewTopic { get; set; } = new();

    public SubjectDetail Subject { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        Input = new SubjectInput { Name = Subject.Name, Description = Subject.Description };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await saveSubject.HandleAsync(new SaveSubjectRequest(Id, Input.Name, Input.Description), cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            return await RedisplayAsync(keepSubjectInput: true, cancellationToken);
        }

        TempData["Flash"] = "Admin.Saved";
        return Redirect($"/admin/subjek/{Id}");
    }

    public async Task<IActionResult> OnPostAddTopicAsync(CancellationToken cancellationToken)
    {
        var result = await saveTopic.HandleAsync(new SaveTopicRequest(null, Id, NewTopic.Name, NewTopic.Description), cancellationToken);
        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(NewTopic));
            return await RedisplayAsync(keepSubjectInput: false, cancellationToken);
        }

        TempData["Flash"] = "Admin.Topics.Added";
        return Redirect($"/admin/subjek/{Id}");
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        var result = await deleteSubject.HandleAsync(Id, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            return await RedisplayAsync(keepSubjectInput: false, cancellationToken);
        }

        TempData["Flash"] = "Admin.Deleted";
        return Redirect("/admin/subjek");
    }

    private async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        var subject = await getSubject.HandleAsync(Id, cancellationToken);
        if (subject is null)
        {
            return false;
        }

        Subject = subject;
        return true;
    }

    private async Task<IActionResult> RedisplayAsync(bool keepSubjectInput, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(cancellationToken))
        {
            return NotFound();
        }

        if (!keepSubjectInput)
        {
            Input = new SubjectInput { Name = Subject.Name, Description = Subject.Description };
        }

        return Page();
    }
}

/// <summary>Topic form fields. Rules live in <see cref="TopicRules"/>.</summary>
public sealed class TopicInput
{
    public long SubjectId { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }
}
