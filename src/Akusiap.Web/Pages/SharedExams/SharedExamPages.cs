using Akusiap.Application.Moderation;
using Akusiap.Web.Forms;
using Akusiap.Web.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace Akusiap.Web.Pages.SharedExams;

public static class SharedExamRoutes
{
    public static string Landing(string token) => $"/s/{Uri.EscapeDataString(token)}";

    public static string Report(string token) => $"{Landing(token)}/lapor";
}

/// <summary>
/// <c>/s/{token}</c> (SHR-02, SHR-03): the shared exam's landing page. A link that doesn't work is 404; a taken-down
/// exam shows "Ujian tidak tersedia".
/// </summary>
public sealed class LandingModel(ExamReports exams) : PageModel
{
    public SharedExamInfo Exam { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string token, CancellationToken cancellationToken)
    {
        var exam = await exams.FindSharedAsync(token, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        Exam = exam;
        return Page();
    }
}

/// <summary><c>/s/{token}/lapor</c> (SHR-02): "Laporkan ujian ini", for anyone holding the link.</summary>
public sealed class ReportModel(ExamReports exams, IStringLocalizer<SharedResource> t) : PageModel
{
    [BindProperty]
    public ExamReportReason? Reason { get; set; }

    [BindProperty]
    public string? Details { get; set; }

    [BindProperty(SupportsGet = true, Name = "terkirim")]
    public bool Sent { get; set; }

    public SharedExamInfo Exam { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string token, CancellationToken cancellationToken)
    {
        var exam = await exams.FindSharedAsync(token, cancellationToken);
        if (exam is null)
        {
            return NotFound();
        }

        Exam = exam;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string token, CancellationToken cancellationToken)
    {
        var result = await exams.ReportAsync(token, Reason, Details, cancellationToken);
        if (result.IsNotFound)
        {
            return NotFound();
        }

        if (!result.Succeeded)
        {
            foreach (var problem in result.Problems)
            {
                ModelState.AddModelError(problem.Field ?? string.Empty, ProblemModelState.Describe(problem, t));
            }

            Exam = (await exams.FindSharedAsync(token, cancellationToken))!;
            return Page();
        }

        return Redirect($"{SharedExamRoutes.Report(token)}?terkirim=true");
    }
}
