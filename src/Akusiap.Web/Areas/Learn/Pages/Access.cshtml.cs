using Akusiap.Application.Access;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Learn.Pages;

/// <summary><c>/learn/akses</c> (ENT-04): the learner's exam purchases and subscriptions with end dates and days left.</summary>
public sealed class AccessModel(GetMyAccess getMyAccess) : PageModel
{
    public MyAccess Access { get; private set; } = new([], []);

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Access = await getMyAccess.HandleAsync(cancellationToken);
}
