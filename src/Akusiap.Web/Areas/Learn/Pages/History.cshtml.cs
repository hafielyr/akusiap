using System.Globalization;
using Akusiap.Application.Sessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary><c>/learn/riwayat</c> (SES-10): the learner's attempts, each opening its result and review, or the player when still open.</summary>
public sealed class HistoryModel(ListHistory listHistory) : PageModel
{
    public const string Route = "/learn/riwayat";

    [BindProperty(SupportsGet = true, Name = "halaman")]
    public int PageNumber { get; set; } = 1;

    public HistoryPage History { get; private set; } = null!;

    public static string LinkTo(int page) => page <= 1 ? Route : $"{Route}?halaman={page.ToString(CultureInfo.InvariantCulture)}";

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        History = await listHistory.HandleAsync(PageNumber, cancellationToken);
}
