using Akusiap.Application.UpcomingTests;
using Akusiap.Domain.Content;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Areas.Admin.Pages.UpcomingTests;

/// <summary>Upcoming test form fields, posted as <c>Input.*</c>. Rules live in <see cref="UpcomingTestRules"/>.</summary>
public sealed class UpcomingTestInput
{
    public string? Slug { get; set; }

    public string? Name { get; set; }

    public string? Organizer { get; set; }

    public UpcomingTestCategory? Category { get; set; }

    public string? Summary { get; set; }

    public DateOnly? RegistrationOpensOn { get; set; }

    public DateOnly? TestStartsOn { get; set; }

    public ScheduleStatus? ScheduleStatus { get; set; } = Domain.Content.ScheduleStatus.Estimated;

    public string? ScheduleNote { get; set; }

    public string? Outline { get; set; }

    public string? SourceUrl { get; set; }

    public long? ExamId { get; set; }

    public bool IsPublished { get; set; }
}

/// <summary>
/// <c>/admin/jadwal-tes/form</c> and <c>/admin/jadwal-tes/form/{id}</c> (CPNS-01): add or edit an upcoming test, link
/// it to its package exam, and delete it.
/// </summary>
public sealed class FormModel(ManageUpcomingTests manage, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public UpcomingTestInput Input { get; set; } = new();

    public IReadOnlyList<PackageExamOption> Exams { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (Id is { } id)
        {
            var test = await manage.GetAsync(id, cancellationToken);
            if (test is null)
            {
                return NotFound();
            }

            Input = new UpcomingTestInput
            {
                Slug = test.Slug,
                Name = test.Name,
                Organizer = test.Organizer,
                Category = test.Category,
                Summary = test.Summary,
                RegistrationOpensOn = test.RegistrationOpensOn,
                TestStartsOn = test.TestStartsOn,
                ScheduleStatus = test.ScheduleStatus,
                ScheduleNote = test.ScheduleNote,
                Outline = test.Outline,
                SourceUrl = test.SourceUrl,
                ExamId = test.ExamId,
                IsPublished = test.IsPublished,
            };
        }

        Exams = await manage.ExamOptionsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var result = await manage.SaveAsync(
            new SaveUpcomingTestRequest(
                Id,
                Input.Slug,
                Input.Name,
                Input.Organizer,
                Input.Category,
                Input.Summary,
                Input.RegistrationOpensOn,
                Input.TestStartsOn,
                Input.ScheduleStatus,
                Input.ScheduleNote,
                Input.Outline,
                Input.SourceUrl,
                Input.ExamId,
                Input.IsPublished),
            cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            ModelState.AddProblems(result.Problems, t, nameof(Input));
            Exams = await manage.ExamOptionsAsync(cancellationToken);
            return Page();
        }

        TempData["Flash"] = "Admin.Saved";
        return Redirect("/admin/jadwal-tes");
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        if (Id is not { } id)
        {
            return NotFound();
        }

        var result = await manage.DeleteAsync(id, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        TempData["Flash"] = "Admin.Deleted";
        return Redirect("/admin/jadwal-tes");
    }
}
