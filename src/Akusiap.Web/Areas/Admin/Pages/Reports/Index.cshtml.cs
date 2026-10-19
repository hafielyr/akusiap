using Akusiap.Application.Moderation;
using Akusiap.Domain.Moderation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Reports;

/// <summary><c>/admin/laporan</c> (SES-11, story ADM-04): Open question reports grouped by question, newest first.</summary>
public sealed class IndexModel(QuestionReports reports) : PageModel
{
    public const string Route = "/admin/laporan";

    public IReadOnlyList<ReportGroup> Groups { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Groups = await reports.OpenGroupsAsync(cancellationToken);

    /// <summary>Closes every Open report on the question as Fixed or Rejected.</summary>
    public async Task<IActionResult> OnPostResolveAsync(long questionId, ContentReportStatus status, CancellationToken cancellationToken)
    {
        if (status is not (ContentReportStatus.Fixed or ContentReportStatus.Rejected))
        {
            return BadRequest();
        }

        await reports.ResolveAsync(questionId, status, cancellationToken);
        TempData["Flash"] = status == ContentReportStatus.Fixed ? "Admin.Reports.MarkedFixed" : "Admin.Reports.MarkedRejected";
        return Redirect(Route);
    }
}
