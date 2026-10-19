using Akusiap.Application.Moderation;
using Akusiap.Domain.Moderation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Reports;

/// <summary>
/// <c>/admin/laporan</c> (SES-11, SHR-02; stories ADM-04, ADM-27): Open reports, newest first. The "Soal" tab groups
/// question reports; the "Ujian" tab (<c>?tab=ujian</c>) groups shared-exam reports, with take down and restore.
/// </summary>
public sealed class IndexModel(QuestionReports questionReports, ExamReports examReports) : PageModel
{
    public const string Route = "/admin/laporan";
    public const string ExamTab = "ujian";

    [BindProperty(SupportsGet = true, Name = "tab")]
    public string? Tab { get; set; }

    public bool ShowExams => Tab == ExamTab;

    public IReadOnlyList<ReportGroup> Groups { get; private set; } = [];

    public IReadOnlyList<ExamReportGroup> ExamGroups { get; private set; } = [];

    public IReadOnlyList<TakenDownExam> TakenDown { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (ShowExams)
        {
            ExamGroups = await examReports.OpenGroupsAsync(cancellationToken);
            TakenDown = await examReports.TakenDownAsync(cancellationToken);
        }
        else
        {
            Groups = await questionReports.OpenGroupsAsync(cancellationToken);
        }
    }

    /// <summary>Closes every Open report on the question as Fixed or Rejected.</summary>
    public async Task<IActionResult> OnPostResolveAsync(long questionId, ContentReportStatus status, CancellationToken cancellationToken)
    {
        if (status is not (ContentReportStatus.Fixed or ContentReportStatus.Rejected))
        {
            return BadRequest();
        }

        await questionReports.ResolveAsync(questionId, status, cancellationToken);
        TempData["Flash"] = status == ContentReportStatus.Fixed ? "Admin.Reports.MarkedFixed" : "Admin.Reports.MarkedRejected";
        return Redirect(Route);
    }

    public Task<IActionResult> OnPostTakeDownAsync(long examId, CancellationToken cancellationToken) =>
        ExamActionAsync(examReports.TakeDownAsync(examId, cancellationToken), "Admin.Reports.TakenDown");

    public Task<IActionResult> OnPostRejectExamAsync(long examId, CancellationToken cancellationToken) =>
        ExamActionAsync(examReports.RejectAsync(examId, cancellationToken), "Admin.Reports.MarkedRejected");

    public Task<IActionResult> OnPostRestoreAsync(long examId, CancellationToken cancellationToken) =>
        ExamActionAsync(examReports.RestoreAsync(examId, cancellationToken), "Admin.Reports.Restored");

    private async Task<IActionResult> ExamActionAsync(Task<bool> action, string flash)
    {
        if (!await action)
        {
            return NotFound();
        }

        TempData["Flash"] = flash;
        return Redirect($"{Route}?tab={ExamTab}");
    }
}
