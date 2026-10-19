using Akusiap.Application.Users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Akusiap.Web.Areas.Admin.Pages.Users;

/// <summary><c>/admin/pengguna/{id}</c> (USR-01): profile, roles, access (managed on /admin/akses) and attempt count.</summary>
public sealed class DetailModel(GetUserDetail getUser) : PageModel
{
    public UserDetail Detail { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await getUser.HandleAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound();
        }

        Detail = detail;
        return Page();
    }
}
