using Akusiap.Application.Abstractions;
using Akusiap.Application.Common;
using Akusiap.Application.Questions.Import;
using Akusiap.Domain.Content;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Akusiap.Web.Areas.Admin.Pages.Questions;

/// <summary>
/// <c>/admin/soal/impor</c> (ADM-T7): download the template, upload a file to check it, then confirm to save every row
/// at once. Nothing is saved until the confirm step, and only when every row is valid.
/// </summary>
[RequestSizeLimit(MaxFileBytes + (64 * 1024))]
public sealed class ImportModel(
    ImportQuestions import,
    IQuestionTemplate template,
    IOptions<ImportOptions> options,
    IStringLocalizer<SharedResource> t) : PageModel
{
    public const string Route = "/admin/soal/impor";
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int PreviewLimit = 20;

    public ImportPreview? Preview { get; private set; }

    public IReadOnlyList<string> Errors { get; private set; } = [];

    [BindProperty]
    public QuestionStatus Status { get; set; } = QuestionStatus.Active;

    public int MaxRows => options.Value.MaxRows;

    public void OnGet()
    {
    }

    public FileContentResult OnGetTemplate() =>
        File(template.Create(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "template-soal-akusiap.xlsx");

    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Fail("Admin.Import.FileRequired");
        }

        if (file.Length > MaxFileBytes)
        {
            return Fail("Admin.Import.FileTooLarge");
        }

        if (Status is not (QuestionStatus.Active or QuestionStatus.Draft))
        {
            Status = QuestionStatus.Active;
        }

        await using var stream = file.OpenReadStream();
        Preview = await import.PreviewAsync(stream, file.FileName, Status, cancellationToken);
        Errors = [.. Preview.FileProblems.Select(Describe)];
        return Page();
    }

    public async Task<IActionResult> OnPostConfirmAsync(Guid key, CancellationToken cancellationToken)
    {
        var result = await import.ConfirmAsync(key, cancellationToken);
        if (!result.Succeeded)
        {
            Errors = [.. result.Problems.Select(Describe)];
            return Page();
        }

        TempData["Flash"] = "Admin.Import.Done";
        TempData["FlashArg"] = result.Value;
        return Redirect("/admin/soal");
    }

    public string Describe(Problem problem) => ProblemModelState.Describe(problem, t);

    private PageResult Fail(string key)
    {
        Errors = [t[key].Value];
        return Page();
    }
}
